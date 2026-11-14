using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class GameScene : Node2D
{
    // Index zero is the joker. Each number can receive its own artwork in the inspector.
    [Export] public Texture2D[] Faces { get; set; } = new Texture2D[11];
    [Export] public Texture2D Back { get; set; }
    [Export] public Texture2D TableArt { get; set; }
    [Export] public Texture2D HandArt { get; set; }

    [Export] public Texture2D PalmArt { get; set; }
    [Export] public Texture2D BodyArt { get; set; }
    [Export] public Texture2D GlassArt { get; set; }
    [Export] public Texture2D ArmArt { get; set; }
    [Export] public Texture2D RivalPawArt { get; set; }

    private sealed class Card(int rank)
    {
        public readonly int Rank = rank;
        public Vector2 Position;
        public float Angle;
        public bool LeavingRivalHand;
    }

    private readonly List<Card> mine = new();
    private readonly List<Card> rival = new();
    private readonly Rect2 bounds = new(-38, -60, 76, 120);
    private readonly RandomNumberGenerator rng = new();
    private readonly List<Card> discarding = new();
    private Card held, match, hoveredRival;
    private bool fromRival, inspecting;
    private static readonly Rect2 GlassBounds = new(684, 439, 139, 113);
    private Vector2 pressPoint;
    private bool dragged;
    private int gap;
    private Vector2 pointer;
    [Export] public OpponentDifficulty Difficulty { get; set; } = OpponentDifficulty.Normal;
    private readonly OpponentAI ai = new();
    private Move move;
    private Card destinationCard;
    private static readonly Vector2 Shoulder = new(420, 214), ArmRest = new(369, 167);
    private const float CatchTime = 0.55f, ReturnStart = 0.68f,
        DepositTime = 1.17f, RestTime = 1.42f;
    private Vector2 paw = ArmRest, reachEnd, deposit;
    private Card targetCard, carried;
    private float reachTime = -1, restTime;

    private static float Phase(float time, float start, float end) =>
        Mathf.SmoothStep(0, 1, Mathf.Clamp((time - start) / (end - start), 0, 1));

    private void UpdateArm(float dt)
    {
        if (countdown > 0 || (result != null && reachTime < 0)) return;
        if (reachTime < 0)
        {
            restTime += dt;
            if (restTime < OpponentAI.Delay(Difficulty)) return;
            var choice = ai.Choose(Difficulty, rival.Select(c => c.Rank).ToArray(), mine.Count, rival.IndexOf(held));
            restTime = 0;
            move = choice.Move;
            if (move == Move.Wait) return;
            targetCard = move == Move.Steal ? mine[choice.From] : rival[choice.From];
            destinationCard = choice.To < 0 ? null : rival[choice.To];
            reachEnd = targetCard.Position;
            reachTime = restTime = 0;
        }
        reachTime += dt;
        if (move != Move.Steal)
        {
            UpdateOwnMove();
            return;
        }
        if (targetCard != null && mine.Contains(targetCard)) reachEnd = targetCard.Position;
        if (reachTime < CatchTime)
            paw = ArmRest.Lerp(reachEnd, Phase(reachTime, 0, CatchTime));
        else
        {
            if (targetCard != null)
            {
                int taken = mine.IndexOf(targetCard);
                if (taken >= 0 && result == null && mine.Remove(targetCard))
                {
                    ai.ObserveRemoval(taken);
                    if (targetCard.Rank == 0) ai.AcquiredJoker();
                    carried = targetCard;
                    if (held == carried) held = match = null;
                    else if (match == carried) match = null;
                }
                targetCard = null;
                deposit = carried != null ? Slot(0, rival.Count + 1, true) : ArmRest;
            }
            if (reachTime < DepositTime)
                paw = reachEnd.Lerp(deposit, Phase(reachTime, ReturnStart, DepositTime));
            else
                paw = deposit.Lerp(ArmRest, Phase(reachTime, DepositTime, RestTime));

            if (carried != null)
            {
                carried.Position = paw + new Vector2(0, 26);
                if (reachTime >= DepositTime)
                {
                    rival.Insert(0, carried);
                    carried = null;
                    CheckResult();
                }
            }
            if (reachTime >= RestTime) reachTime = -1;
        }
    }

    private void UpdateOwnMove()
    {
        // Revalidate both cards: the player may have picked one up during the approach.
        const float contact = 0.32f, place = 0.67f, finish = 0.91f;
        if (reachTime < contact)
        {
            if (targetCard != held && rival.Contains(targetCard)) reachEnd = targetCard.Position;
            paw = ArmRest.Lerp(reachEnd, Phase(reachTime, 0, contact));
            return;
        }
        if (targetCard != null && reachTime < place)
        {
            deposit = destinationCard != held && rival.Contains(destinationCard) ? destinationCard.Position : reachEnd;
            paw = reachEnd.Lerp(deposit, Phase(reachTime, contact, place));
            if (result == null && rival.Contains(targetCard) && held != targetCard)
                targetCard.Position = paw;
            return;
        }
        if (targetCard != null)
        {
            if (result == null && held != targetCard && held != destinationCard &&
                rival.Contains(targetCard) && rival.Contains(destinationCard))
            {
                int to = rival.IndexOf(destinationCard);
                rival.Remove(targetCard);
                if (move == Move.Pair)
                {
                    rival.Remove(destinationCard);
                    targetCard.LeavingRivalHand = destinationCard.LeavingRivalHand = true;
                    FlyToCenter(targetCard);
                    FlyToCenter(destinationCard);
                    CheckResult();
                }
                else rival.Insert(to, targetCard);
            }
            targetCard = destinationCard = null;
        }
        paw = deposit.Lerp(ArmRest, Phase(reachTime, place, finish));
        if (reachTime >= finish) reachTime = -1;
    }

    private static float ForegroundScale(float y) => 1 + 0.65f * Phase(y, 205, 505);

    private void Sleeve(Vector2 start, Vector2 bend, Vector2 end, bool approaching = false)
    {
        if (ArmArt == null) return;
        const int steps = 12;
        const int side = steps + 1;
        var outline = new Vector2[2 * side];
        var uv = new Vector2[2 * side];
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            Vector2 a = start.Lerp(bend, t), b = bend.Lerp(end, t);
            Vector2 normal = (b - a).Normalized().Orthogonal() * Mathf.Lerp(18, 15, t);
            Vector2 point = a.Lerp(b, t);
            if (approaching) normal *= ForegroundScale(point.Y);
            int opposite = 2 * side - 1 - i;
            outline[i] = point + normal;
            outline[opposite] = point - normal;
            uv[i] = new(0, t);
            uv[opposite] = new(1, t);
        }
        for (int i = 0; i < steps; i++)
        {
            int j = outline.Length - 1 - i;
            DrawPrimitive(new[] { outline[i], outline[i+1], outline[j] },
                new[] { Colors.White }, new[] { uv[i], uv[i+1], uv[j] }, ArmArt);
            DrawPrimitive(new[] { outline[i+1], outline[j-1], outline[j] },
                new[] { Colors.White }, new[] { uv[i+1], uv[j-1], uv[j] }, ArmArt);
        }
    }

    private void DrawShoulder()
    {
        Vector2 elbow = Shoulder.Lerp(paw, 0.55f) + new Vector2(-36, 62);
        Sleeve(Shoulder, elbow, paw, approaching: true);
    }
    private void CheckResult()
    {
        if (mine.Count == 1 && mine[0].Rank == 0) result = "WIN";
        if (rival.Count == 1 && rival[0].Rank == 0 && carried == null) result = "LOSE";
    }

    private float countdown = 3;
    private string result;
    private static readonly Color Ink = new("#242238"), Paper = new("#fff0cf"), Gold = new("#ffcd70");
    private Vector2 center => new(480, 302);

    public override void _Ready()
    {
        foreach (int rank in new[] { 3, 7, 1, 5, 3, 6, 8, 7, 3, 9 }) mine.Add(new Card(rank));
        foreach (int rank in new[] { 2, 6, 4, 10, 0, 5, 8, 9, 2, 6 }) rival.Add(new Card(rank));
        Fit();
        Layout(1);
    }

    private void Fit()
    {
        Vector2 view = GetViewportRect().Size;
        float scale = Mathf.Min(view.X / 960, view.Y / 600);
        Scale = Vector2.One * scale;
        Position = (view - new Vector2(960, 600) * scale) / 2;
    }

    public override void _Process(double delta)
    {
        Fit();
        pointer = GetLocalMousePosition();
        countdown = Mathf.Max(0, countdown - (float)delta);
        if (held != null) Aim();
        UpdateArm((float)delta);
        hoveredRival = !inspecting && held == null && result == null && countdown <= 0 ? RivalAtPointer() : null;
        Layout(1 - Mathf.Exp(-18 * (float)delta));
        QueueRedraw();
    }

    private static Vector2 Slot(int i, int total, bool upper)
    {
        float t = i - (total - 1) / 2f;
        float arc = Mathf.Pow(t / Mathf.Max(1, (total - 1) / 2f), 2);
        return new((upper ? 505 : 300) + t * Mathf.Min(upper ? 19 : 29, 470f / Mathf.Max(1, total - 1)),
                   upper ? 198 + arc * 18 : 484 + arc * 23);
    }

    private Card RivalAtPointer()
    {
        // Resting positions keep the cursor on the same card while its neighbours slide.
        for (int i = rival.Count - 1; i >= 0; i--)
        {
            float angle = (i - (rival.Count - 1) / 2f) * 0.072f;
            Vector2 local = (pointer - Slot(i, rival.Count, true)).Rotated(-angle);
            if (bounds.HasPoint(local)) return rival[i];
        }
        return null;
    }

    private void Layout(float weight)
    {
        foreach (bool upper in new[] { false, true })
        {
            var cards = upper ? rival : mine;
            int count = cards.Count(c => c != held);
            bool opening = !upper && held != null && !fromRival && match == null;
            int slots = count + (opening ? 1 : 0);
            int focus = upper && hoveredRival != null ? rival.IndexOf(hoveredRival) : -1;
            if (!upper && held == null)
            {
                // Use resting positions so the opening gap cannot move its own trigger.
                for (int n = 0; n < count; n++)
                {
                    Vector2 local = pointer - Slot(n, count, false);
                    if (bounds.Grow(9).HasPoint(local)) focus = n;
                }
            }
            int i = 0;
            foreach (Card card in cards)
            {
                if (card == held) continue;
                if (opening && i == gap) i++;
                Vector2 destination = Slot(i, slots, upper);
                if (focus >= 0)
                {
                    int distance = i - focus;
                    destination.X += Mathf.Sign(distance) * Mathf.Max(0, 31 - 7 * Mathf.Abs(distance));
                    if (!upper && distance == 0) destination.Y -= 8;
                }
                card.Position = card.Position.Lerp(destination, weight);
                card.Angle = Mathf.Lerp(card.Angle, (i - (slots - 1) / 2f) * (upper ? 0.072f : 0.022f), weight);
                i++;
            }
        }
    }

    private Card Pick(List<Card> cards)
    {
        return cards.LastOrDefault(c => c != held && bounds.HasPoint((pointer - c.Position).Rotated(-c.Angle)));
    }

    public override void _Input(InputEvent input)
    {
        if (input is not InputEventMouseButton button ||
            button.ButtonIndex != MouseButton.Left) return;
        pointer = GetGlobalTransformWithCanvas().AffineInverse() * button.Position;
        if (inspecting)
        {
            if (!button.Pressed) inspecting = false;
            QueueRedraw();
            return;
        }
        if (countdown > 0 || result != null) return;
        if (button.Pressed && held == null && GlassBounds.HasPoint(pointer))
        {
            inspecting = true;
            hoveredRival = null;
            QueueRedraw();
            return;
        }
        if (button.Pressed && held == null)
        {
            pressPoint = pointer;
            dragged = false;
            held = Pick(mine);
            fromRival = held == null;
            if (fromRival) held = RivalAtPointer();
            hoveredRival = null;
            if (held != null) Aim();
        }
        else if (!button.Pressed && held != null)
        {
            Aim();
            if (fromRival)
            {
                if (pointer.Y >= 355 && pointer.Y <= 600 && pointer.X >= 0 && pointer.X <= 960)
                {
                    rival.Remove(held);
                    int slot = rng.RandiRange(0, mine.Count);
                    ai.ObserveInsertion(slot, held.Rank == 0);
                    mine.Insert(slot, held);
                }
            }
            else if (match != null)
            {
                Discard(match);
                Discard(held);
            }
            else if (dragged && pointer.Y >= 355)
            {
                ai.ObserveReorder(mine.IndexOf(held), gap, Difficulty);
                mine.Remove(held);
                mine.Insert(gap, held);
            }
            held = match = null;
            CheckResult();
        }
        QueueRedraw();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut)
        {
            inspecting = false;
            held = match = null;
        }
    }

    private void Aim()
    {
        int others = mine.Count(c => c != held);
        gap = 0;
        while (gap < others && pointer.X > Slot(gap, others, false).X) gap++;
        dragged |= pointer.DistanceSquaredTo(pressPoint) >= 36;
        match = dragged && !fromRival && held.Rank > 0 ? NearbyMatch() : null;
        held.Position = match == null ? pointer : match.Position + new Vector2(5,-5);
        held.Angle = 0;
    }
    private Card NearbyMatch()
    {
        // Probe visible card faces so a buried card cannot snap through its neighbour.
        Card closest = null;
        float best = float.PositiveInfinity;
        foreach (var offset in SnapOffsets)
        {
            var probe = pointer + offset;
            var top = mine.LastOrDefault(c => c != held &&
                bounds.HasPoint((probe - c.Position).Rotated(-c.Angle)));
            if (top?.Rank != held.Rank || offset.LengthSquared() >= best) continue;
            best = offset.LengthSquared();
            closest = top;
            if (best == 0) break;
        }
        return closest;
    }

    private static readonly Vector2[] SnapOffsets = {
        Vector2.Zero, new(-7,0), new(7,0), new(0,-7), new(0,7),
        new(-10,-10), new(10,-10), new(-10,10), new(10,10),
        new(-15,0), new(15,0), new(0,-15), new(0,15)
    };

    private void Discard(Card card)
    {
        ai.ObserveRemoval(mine.IndexOf(card));
        mine.Remove(card);
        FlyToCenter(card);
    }

    private void FlyToCenter(Card card)
    {
        discarding.Add(card);
        var tween = CreateTween();
        tween.TweenMethod(Callable.From<Vector2>(p => card.Position = p), card.Position, center, 0.32);
        tween.TweenCallback(Callable.From(() => discarding.Remove(card)));
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, 960, 600), Ink);
        DrawRect(new Rect2(0,295,960,305), new Color("#48446b"));
        if (TableArt != null) DrawTextureRect(TableArt,new Rect2(0,0,960,600),false);
        if (BodyArt != null) DrawTextureRect(BodyArt,new Rect2(372,48,238,248),false);
        if (GlassArt != null) DrawTextureRect(GlassArt, GlassBounds, false);
        DrawShoulder();
        foreach (var card in rival) if (card != held) Paint(card, true);
        if (RivalPawArt != null)
        {
            DrawSetTransform(new Vector2(565, 257), Mathf.Pi, Vector2.One);
            DrawTextureRect(RivalPawArt, new Rect2(-35, -28, 70, 56), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
        foreach (var card in mine) if (card != held) Paint(card, false);
        foreach (var card in discarding) Paint(card, false);
        if (held != null) Paint(held, fromRival);
        if (carried != null) Paint(carried, false);
        var activePaw = reachTime < 0 || reachTime >= DepositTime ? PalmArt : RivalPawArt;
        if (activePaw != null)
        {
            Vector2 size = new Vector2(66, 58) * ForegroundScale(paw.Y);
            DrawTextureRect(activePaw, new Rect2(paw - size / 2, size), false);
        }
        if (HandArt != null)
        {
            Vector2 tip = new Rect2(0,0,960,600).HasPoint(pointer) ? pointer : new Vector2(738,341);
            DrawTextureRect(HandArt,new Rect2(tip - new Vector2(6,25),new Vector2(126,105)),false);
            // The other paw supports the player hand from underneath.
            DrawTextureRect(HandArt,new Rect2(204,520,162,135),false);
        }
        if (inspecting && result == null) DrawMagnifier();
        if (countdown > 0 || result != null)
            DrawString(ThemeDB.FallbackFont,new(0,325),result ?? Mathf.CeilToInt(countdown).ToString(),HorizontalAlignment.Center,960,52,Gold);
    }

    private void DrawMagnifier()
    {
        Vector2 lens = new(480, 300);
        DrawRect(new Rect2(Vector2.Zero, new Vector2(960, 600)), new Color("#171524eb"));
        DrawSetTransform(lens, 0, new Vector2(1.10f, 1));
        DrawCircle(Vector2.Zero, 409, new Color("#b8b0c7"), true, -1, true);
        DrawCircle(Vector2.Zero, 401, new Color("#45445f"), true, -1, true);
        DrawArc(Vector2.Zero, 389, 3.4f, 4.22f, 50, new Color("#bbb8d6"), 3, true);

        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        int count = rival.Count;
        float half = Mathf.Max(1, (count - 1) * 0.5f);
        float tableSpacing = Mathf.Min(19, 470f / Mathf.Max(1, count - 1));
        float lensSpacing = Mathf.Min(91, 640f / Mathf.Max(1, count - 1));

        // Project the animated table pose, including cards between slots.
        void DrawLiveCard(Card card)
        {
            float x = Mathf.Clamp((card.Position.X - 505) / tableSpacing * lensSpacing, -320, 320);
            float y = Mathf.Clamp(-35 + (card.Position.Y - 198) * (44f / 18), -65, 100);
            float angle = Mathf.Clamp(card.Angle / (half * 0.072f), -1, 1) * 0.19f;
            // Keep even the tilted outer corners of a moving card within the lens.
            float maxX = 441 * Mathf.Sqrt(1 - Mathf.Pow((Mathf.Abs(y) + 135) / 401f, 2)) - 101;
            x = Mathf.Clamp(x, -maxX, maxX);
            DrawSetTransform(lens + new Vector2(x, y), angle, Vector2.One * 1.85f);
            if (card.Rank == 0)
            {
                DrawRect(bounds.Grow(5), new Color("#ffda793d"));
                DrawRect(bounds.Grow(2), Gold);
            }
            DrawRect(bounds, new Color("#d9d5e2bd"));
            Texture2D art = Faces != null && card.Rank < Faces.Length ? Faces[card.Rank] : null;
            if (art != null)
                DrawTextureRect(art, bounds, false, new Color(1, 1, 1, 0.74f));
            else
                DrawString(ThemeDB.FallbackFont, new(-38, 9), card.Rank == 0 ? "JOKER" : card.Rank.ToString(),
                    HorizontalAlignment.Center, 76, card.Rank == 0 ? 20 : 30, Ink);
            DrawRect(bounds, card.Rank == 0 ? Gold : new Color("#f2e9d7"), false, 1.5f);
            // Keep the exposed corner readable where neighbouring cards overlap.
            DrawString(ThemeDB.FallbackFont, new(-34, -40), card.Rank == 0 ? "J" : card.Rank.ToString(),
                fontSize: 16, modulate: Ink);
        }
        Card moving = reachTime >= 0 && move != Move.Steal ? targetCard : null;
        foreach (Card card in rival)
            if (card != moving) DrawLiveCard(card);
        if (moving != null && rival.Contains(moving)) DrawLiveCard(moving);
        foreach (Card card in discarding)
            if (card.LeavingRivalHand) DrawLiveCard(card);
        if (carried != null && paw.Y < 290) DrawLiveCard(carried);
        DrawSetTransform(lens + new Vector2(0, 104), Mathf.Pi, Vector2.One);
        if (RivalPawArt != null) DrawTextureRect(RivalPawArt, new Rect2(-87, -69, 174, 138), false);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
    private void Paint(Card card, bool hidden)
    {
        DrawSetTransform(card.Position,card.Angle,Vector2.One);
        DrawRect(bounds.Grow(2), Ink);
        DrawRect(bounds,hidden ? new Color("#85607d") : Paper);
        Texture2D tex = hidden ? Back : Faces != null && card.Rank < Faces.Length ? Faces[card.Rank] : null;
        if (tex != null) DrawTextureRect(tex,bounds,false);
        else if (hidden)
        {
            DrawRect(bounds.Grow(-5),Gold,false,2);
            DrawColoredPolygon(new Vector2[]{new(0,-19),new(12,0),new(0,19),new(-12,0)},Gold);
        }
        else
        {
            string label = card.Rank == 0 ? "J" : card.Rank.ToString();
            DrawString(ThemeDB.FallbackFont,new(-32,-39),label,fontSize:16,modulate:Ink);
            DrawString(ThemeDB.FallbackFont,new(-38,10),card.Rank == 0 ? "JOKER" : label,HorizontalAlignment.Center,76,card.Rank == 0 ? 20 : 32,Ink);
        }
        if (hidden && (card == hoveredRival || (card == held && fromRival)))
        {
            DrawRect(bounds, new Color(1f, 0.90f, 0.69f, 0.12f));
            DrawRect(bounds.Grow(1), new Color(1f, 0.86f, 0.58f, 0.8f), false, 2);
        }
        if (held != null && card != held && !hidden && !fromRival && held.Rank > 0 && card.Rank == held.Rank)
            DrawRect(bounds.Grow(1),Gold,false,4);
        DrawSetTransform(Vector2.Zero,0,Vector2.One);
    }
}
