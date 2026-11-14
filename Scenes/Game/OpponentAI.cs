using Godot;
using System;
using System.Linq;

public enum OpponentDifficulty { Easy, Normal, Hard }
internal enum Move { Wait, Steal, Pair, Shuffle, Inspect }
internal readonly record struct Decision(Move Move, int From = -1, int To = -1);

internal sealed class OpponentAI
{
    private readonly RandomNumberGenerator rng = new();
    private int lastSeen = -1;
    private bool newlyAcquired;
    private int inspectCooldown;
    private int inspectUses;

    public int InspectUses => inspectUses;
    public void GainInspectUse() => inspectUses++;
    public void RememberInspection(int jokerIndex) => lastSeen = jokerIndex;

    public static float Delay(OpponentDifficulty level) => level switch
    {
        OpponentDifficulty.Easy => 2f, OpponentDifficulty.Hard => 0.5f, _ => 1f
    };

    public void ObserveInsertion(int index, bool joker)
    {
        if (joker) lastSeen = index;
        else if (index <= lastSeen) lastSeen++;
    }

    public void ObserveRemoval(int index)
    {
        if (index == lastSeen) lastSeen = -1;
        else if (index < lastSeen) lastSeen--;
    }

    public void ObserveReorder(int from, int to, OpponentDifficulty level)
    {
        if (lastSeen < 0 || from == to) return;
        float attention = level switch { OpponentDifficulty.Easy => 0.12f, OpponentDifficulty.Hard => 0.85f, _ => 0.5f };
        if (rng.Randf() > attention) return;
        if (lastSeen == from) lastSeen = to;
        else if (from < lastSeen && to >= lastSeen) lastSeen--;
        else if (from > lastSeen && to <= lastSeen) lastSeen++;
    }

    public void AcquiredJoker() { newlyAcquired = true; lastSeen = -1; }

    // The player's cards are hidden: only the count and previously observed movements enter this decision.
    public Decision Choose(OpponentDifficulty level, int[] own, int playerCount, int busy)
    {
        if (inspectCooldown > 0) inspectCooldown--;
        int joker = Array.IndexOf(own, 0);
        var pair = own.Select((rank, index) => (rank, index))
            .Where(c => c.index != busy && c.rank != 0)
            .GroupBy(c => c.rank).FirstOrDefault(g => g.Count() >= 2);
        bool winPair = joker >= 0 && own.Length == 3 && pair != null;
        float hide = level switch { OpponentDifficulty.Easy => 0.07f, OpponentDifficulty.Hard => 0.38f, _ => 0.21f };
        if (!winPair && joker >= 0 && joker != busy && own.Length > 1 &&
            ((newlyAcquired && level != OpponentDifficulty.Easy) || rng.Randf() < hide))
        {
            newlyAcquired = false;
            int other = rng.RandiRange(0, own.Length - 2);
            if (other >= joker) other++;
            bool decoy = other != busy && level != OpponentDifficulty.Easy &&
                rng.Randf() < (level == OpponentDifficulty.Hard ? 0.55f : 0.22f);
            return decoy ? new(Move.Shuffle, other, joker) : new(Move.Shuffle, joker, other);
        }
        if (pair != null && (winPair || playerCount == 0 || rng.Randf() <
            (level == OpponentDifficulty.Easy ? 0.4f : level == OpponentDifficulty.Normal ? 0.65f : 0.82f)))
            return new(Move.Pair, pair.First().index, pair.Skip(1).First().index);
        if (playerCount == 0) return new(Move.Wait);
        float curiosity = level switch { OpponentDifficulty.Easy => 0.18f, OpponentDifficulty.Hard => 0.55f, _ => 0.35f };
        if (inspectUses > 0 && joker < 0 && playerCount > 1 && inspectCooldown == 0 && rng.Randf() < curiosity)
        {
            inspectUses--;
            inspectCooldown = 4;
            return new(Move.Inspect);
        }
        float recall = level switch { OpponentDifficulty.Easy => 0.1f, OpponentDifficulty.Hard => 0.88f, _ => 0.55f };
        int pick = lastSeen >= 0 && lastSeen < playerCount && rng.Randf() < recall ? lastSeen : rng.RandiRange(0, playerCount - 1);
        return new(Move.Steal, pick);
    }
}
