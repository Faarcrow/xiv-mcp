using System;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using XivMcp.Tools;

namespace XivMcp.Util;

/// <summary>
/// The delivery that ranks a client up, handed in by XIV MCP for player_ranks_up the way a player does it: talk to the client, pick
/// the request in its delivery window, put the item in and press Hand Over. Everything after Hand Over is the client's story, which
/// is the player's: nothing is clicked from then on. Satisfier is not used for it because it clicks through every line of dialogue
/// while it turns in. One look and at most one action per <see cref="Step"/>, which is meant to be called a few times a second;
/// framework thread.
/// </summary>
internal sealed class RankUpHandIn(SatisfierBridge.Client client, int slot, uint itemId)
{
    public enum Next { Wait, HandedOver, Failed }

    private const int MaxTries = 3;
    private int talks, delivers, fills;

    /// <summary>How long a window that was acted on gets to answer before it is acted on again.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(1.5);

    private enum Window { Client, Menu, Supply, Request }
    private Window? acted;
    private DateTime actedAt;

    /// <summary>The window was just acted on and may still be answering: acting again could do it twice.</summary>
    private bool Answering(Window window) => acted == window && DateTime.UtcNow - actedAt < Settle;

    private void Acted(Window window)
    {
        acted = window;
        actedAt = DateTime.UtcNow;
    }

    /// <summary>Why the hand-in failed (<see cref="Next.Failed"/>), for the player who is asked to do it instead.</summary>
    public string? Problem { get; private set; }

    public unsafe Next Step()
    {
        // A question before the hand-over (the game asks e.g. when the scrips would cap) is the player's to answer.
        if (RetainerUi.Ready("SelectYesno")) return Fail("the game asked a question");

        var trade = AgentNpcTrade.Instance();
        if (trade != null && trade->IsAgentActive())
        {
            var request = (AddonRequest*)Svc.GameGui.GetAddonByName<AtkUnitBase>("Request", 1);
            if (request == null || !request->AtkUnitBase.IsVisible || !request->AtkUnitBase.IsReady) return Next.Wait;
            if (VentureTools.Click(&request->AtkUnitBase, request->HandOverButton)) return Next.HandedOver;
            // The item list for the request is open, or the item is on its way into the slot.
            if (trade->SelectedTurnInSlot >= 0 || RetainerUi.Ready("ContextIconMenu") || Answering(Window.Request)) return Next.Wait;
            var index = RequestIndex();
            if (index < 0) return Fail("the hand-over window does not ask for the item");
            if (++fills > MaxTries) return Fail("the item did not go into the hand-over window");
            Acted(Window.Request);
            return Fill(trade, index) ? Next.Wait : Fail("the item could not be picked for the hand-over window");
        }

        // The client's lines before the delivery window (the story only starts after Hand Over).
        if (RetainerUi.Ready("Talk"))
        {
            RetainerUi.ClickTalk();
            return Next.Wait;
        }

        var supply = AgentSatisfactionSupply.Instance();
        if (supply != null && supply->IsAgentActive() && supply->NpcInfo.Id == client.Index + 1)
        {
            var window = RaptureAtkUnitManager.Instance()->GetAddonById((ushort)supply->AddonId);
            if (window == null || !window->IsVisible || !window->IsReady || RetainerUi.Ready("SelectString") || Answering(Window.Supply))
                return Next.Wait;
            if (++delivers > MaxTries) return Fail("the delivery window did not open the hand-over");
            Acted(Window.Supply);
            Deliver(supply, slot);
            return Next.Wait;
        }

        // The client's menu: its first entry opens the delivery window.
        if (RetainerUi.Ready("SelectString"))
        {
            if (Answering(Window.Menu)) return Next.Wait;
            Acted(Window.Menu);
            RetainerUi.SelectMenuIndex(0);
            return Next.Wait;
        }

        if (Svc.Condition[ConditionFlag.OccupiedInEvent] || Svc.Condition[ConditionFlag.OccupiedInQuestEvent] ||
            Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent] || Svc.Condition[ConditionFlag.Occupied] || Answering(Window.Client))
            return Next.Wait;
        var me = Svc.Objects.LocalPlayer;
        var npc = Svc.Objects
            .Where(o => o.ObjectKind == ObjectKind.EventNpc && o.IsTargetable && o.Name.TextValue.Equals(client.Name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(o => me is null ? 0 : System.Numerics.Vector3.Distance(o.Position, me.Position))
            .FirstOrDefault();
        if (npc is null || me is null) return Fail($"{client.Name} is not nearby");
        if (System.Numerics.Vector3.Distance(npc.Position, me.Position) > 6f) return Fail($"the character is not next to {client.Name}");
        if (++talks > MaxTries) return Fail($"talking to {client.Name} did not open the delivery window");
        Acted(Window.Client);
        InteractionTools.Interact(npc.Address, lineOfSight: true);
        return Next.Wait;
    }

    private Next Fail(string problem)
    {
        Problem = problem;
        return Next.Failed;
    }

    private unsafe int RequestIndex()
    {
        ref var requests = ref UIState.Instance()->NpcTrade.Requests;
        for (var i = 0; i < requests.Count; i++)
            if (requests.Items[i].ItemId == itemId) return i;
        return -1;
    }

    /// <summary>Picks the request's slot, then the first item the game offers for it, as clicking the slot and the item does.</summary>
    private static unsafe bool Fill(AgentNpcTrade* trade, int index)
    {
        var result = new AtkValue();
        var values = stackalloc AtkValue[4];
        values[0].SetInt(2);
        values[1].SetInt(index);
        values[2].SetInt(0);
        values[3].SetInt(0);
        trade->ReceiveEvent(&result, values, 4, 0);
        if (trade->SelectedTurnInSlot != index || trade->SelectedTurnInSlotItemOptions <= 0) return false;
        values[0].SetInt(0);
        values[1].SetInt(0);
        trade->ReceiveEvent(&result, values, 4, 1);
        return true;
    }

    /// <summary>Picks a request in the client's delivery window (0 crafting, 1 gathering, 2 fishing), as clicking it does.</summary>
    private static unsafe void Deliver(AgentSatisfactionSupply* supply, int slot)
    {
        var result = new AtkValue();
        var values = stackalloc AtkValue[2];
        values[0].SetInt(1);
        values[1].SetInt(slot);
        supply->ReceiveEvent(&result, values, 2, 0);
    }
}
