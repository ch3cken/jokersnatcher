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
    [Export] public Texture2D RivalGlassArt { get; set; }
    [Export] public Texture2D GlassArt { get; set; }
    [Export] public Texture2D BackgroundArt { get; set; }
    [Export] public Texture2D PartyArt { get; set; }
    [Export] public Texture2D PartyShadow { get; set; }
    [Export] public Texture2D LampArt { get; set; }
    [Export] public Texture2D FaceArt { get; set; }
    [Export] public Texture2D SleeveBase { get; set; }
    [Export] public Texture2D SupportArt { get; set; }
    [Export] public Texture2D ClosedHandArt { get; set; }
    [Export] public Texture2D PlayerSupport { get; set; }
    [Export] public Texture2D ThumbArt { get; set; }
    [Export] public Texture2D[] SecondFaces { get; set; } = new Texture2D[11];
    [Export] public Texture2D RivalPawArt { get; set; }

    private sealed class Card(int rank, bool second = false)
    {
        public readonly int Rank = rank;
        public readonly bool Second = second;
        public Vector2 Position;
        public float Angle;
        public bool LeavingRivalHand;
    }

    private readonly List<Card> mine = new();
    private readonly List<Card> rival = new();
    private readonly Rect2 bounds = new(-38, -60, 76, 120);
    private readonly RandomNumberGenerator rng = new();
    private readonly List<Card> discarding = new();
    private Card held, match, hoveredRival, hoveredMine;
    private bool fromRival, inspecting;
    private static readonly Rect2 GlassBounds = new(647, 455, 251, 143);
    private Vector2 pressPoint;
    private bool dragged;
    private int gap;
    private Vector2 pointer;
    [Export] public OpponentDifficulty Difficulty { get; set; } = OpponentDifficulty.Normal;
    private OpponentAI ai;
    private Move move;
    private Card destinationCard;
    private static readonly Vector2 Shoulder = new(415, 291), ArmRest = new(355, 241);
    private const float CatchTime = 0.55f, ReturnStart = 0.68f,
        DepositTime = 1.17f, RestTime = 1.42f;
    private Vector2 paw = ArmRest, reachEnd, deposit;
    private Card targetCard, carried;
    private int insertionSlot;
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
            if (move != Move.Inspect)
            {
                targetCard = move == Move.Steal ? mine[choice.From] : rival[choice.From];
                destinationCard = choice.To < 0 ? null : rival[choice.To];
                reachEnd = targetCard.Position + GripOffset(targetCard.Angle, move == Move.Steal ? PlayerScale : RivalScale);
            }
            reachTime = restTime = 0;
        }
        reachTime += dt;
        if (move == Move.Inspect)
        {
            Vector2 eye = new(419, 185);
            paw = ArmRest.Lerp(eye, Phase(reachTime, 0, 0.24f) * (1 - Phase(reachTime, 0.94f, 1.2f)));
            if (reachTime - dt < 0.9f && reachTime >= 0.9f && result == null)
                ai.RememberInspection(mine.FindIndex(card => card.Rank == 0));
            if (reachTime >= 1.2f) reachTime = -1;
            return;
        }
        if (move != Move.Steal)
        {
            UpdateOwnMove();
            return;
        }
        if (targetCard != null && mine.Contains(targetCard))
            reachEnd = targetCard.Position + GripOffset(targetCard.Angle, PlayerScale);
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
                    pickupAngle = carried.Angle;
                    if (held == carried) held = match = null;
                    else if (match == carried) match = null;
                }
                targetCard = null;
                deposit = ArmRest;
                if (carried != null) insertionSlot = rng.RandiRange(0, rival.Count);
            }
            if (carried != null)
            {
                // The player can draw from this hand while the paw is returning.
                insertionSlot = Mathf.Min(insertionSlot, rival.Count);
                int count = rival.Count + 1 - (rival.Contains(held) ? 1 : 0);
                float slotAngle = (RivalGapIndex - (count - 1) / 2f) * 0.102f;
                carried.Angle = Mathf.Lerp(pickupAngle, slotAngle, Phase(reachTime, ReturnStart, DepositTime));
                deposit = Slot(RivalGapIndex, count, true) + GripOffset(slotAngle, RivalScale);
            }
            if (reachTime < DepositTime)
                paw = reachEnd.Lerp(deposit, Phase(reachTime, ReturnStart, DepositTime));
            else
                paw = deposit.Lerp(ArmRest, Phase(reachTime, DepositTime, RestTime));

            if (carried != null)
            {
                carried.Position = paw - GripOffset(carried.Angle, CarriedScale);
                if (reachTime >= DepositTime)
                {
                    rival.Insert(insertionSlot, carried);
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
            if (targetCard != held && rival.Contains(targetCard))
                reachEnd = targetCard.Position + GripOffset(targetCard.Angle, RivalScale);
            paw = ArmRest.Lerp(reachEnd, Phase(reachTime, 0, contact));
            return;
        }
        if (targetCard != null && reachTime < place)
        {
            deposit = destinationCard != held && rival.Contains(destinationCard)
                ? destinationCard.Position + GripOffset(targetCard.Angle, RivalScale) : reachEnd;
            paw = reachEnd.Lerp(deposit, Phase(reachTime, contact, place));
            if (result == null && rival.Contains(targetCard) && held != targetCard)
                targetCard.Position = paw - GripOffset(targetCard.Angle, RivalScale);
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
    private float pickupAngle;
    private float CarriedScale => Mathf.Lerp(PlayerScale, RivalScale, Phase(reachTime, ReturnStart, DepositTime));
    private Vector2 GripOffset(float angle, float scale) =>
        new Vector2(0, bounds.Position.Y * scale).Rotated(angle);

    private void Layer(Texture2D texture)
    {
        if (texture != null) DrawTextureRect(texture, new Rect2(0, 0, 960, 600), false);
    }

    private Texture2D Face(Card card)
    {
        var images = card.Second ? SecondFaces : Faces;
        return images != null && card.Rank < images.Length ? images[card.Rank] : null;
    }

    private void DrawReachingArm()
    {
        if (reachTime < 0) return;
        Vector2 bend = Shoulder.Lerp(paw, 0.45f) + new Vector2(-36, 45);
        Vector2 previous = Shoulder;
        for (int i = 0; i < 12; i++)
        {
            float t = i / 11f;
            Vector2 point = Shoulder.Lerp(bend, t).Lerp(bend.Lerp(paw, t), t);
            float radius = 16 * ForegroundScale(point.Y);
            if (i > 0) DrawLine(previous, point, new Color("#efc797"), radius * 2);
            DrawCircle(point, radius, new Color("#efc797"));
            previous = point;
        }
    }
    private void CheckResult()
    {
        if (mine.Count == 1 && mine[0].Rank == 0) result = "WIN";
        if (rival.Count == 1 && rival[0].Rank == 0 && carried == null) result = "LOSE";
        retry.Visible = result != null;
    }

    private readonly Button retry = new()
    {
        Text = "RETRY", Visible = false, Position = new Vector2(404, 344),
        Size = new Vector2(152, 46), MouseDefaultCursorShape = Control.CursorShape.PointingHand
    };
    private float countdown = 3;
    private string result;
    private static readonly Color Ink = new("#242238"), Paper = new("#fff0cf"), Gold = new("#ffcd70");
    private Vector2 center => new(480, 302);

    public override void _Ready()
    {
        TintFaces(Faces, new Color("#58504b"));
        TintFaces(SecondFaces, new Color("#ac6257"));
        if (Back != null) Back = WarmCardBack(Back);
        AddChild(retry);
        retry.AddThemeFontSizeOverride("font_size", 22);
        retry.Pressed += StartRound;
        StartRound();
    }

    // Palette-map the supplied card illustrations once; keep their suits and lettering.
    private static void TintFaces(Texture2D[] faces, Color suitInk)
    {
        if (faces == null) return;
        for (int i = 0; i < faces.Length; i++)
        {
            if (faces[i] == null) continue;
            Image image = faces[i].GetImage();
            if (image.IsCompressed()) image.Decompress();
            image.Resize(76, 120, Image.Interpolation.Lanczos);
            Color paper = new("#f7e2c4"), ink = i == 0 ? new Color("#ac805d") : suitInk;
            for (int y = 0; y < image.GetHeight(); y++)
            for (int x = 0; x < image.GetWidth(); x++)
            {
                Color pixel = image.GetPixel(x, y);
                Color tint = ink.Lerp(paper, Mathf.Min(pixel.R, Mathf.Min(pixel.G, pixel.B)));
                tint.A = pixel.A;
                image.SetPixel(x, y, tint);
            }
            faces[i] = ImageTexture.CreateFromImage(image);
        }
    }
    private static Texture2D WarmCardBack(Texture2D source)
    {
        Image image = source.GetImage();
        if (image.IsCompressed()) image.Decompress();
        image.Resize(152, 240, Image.Interpolation.Lanczos);
        for (int y = 0; y < image.GetHeight(); y++)
        for (int x = 0; x < image.GetWidth(); x++)
        {
            Color pixel = image.GetPixel(x, y);
            float light = 0.2126f * pixel.R + 0.7152f * pixel.G + 0.0722f * pixel.B;
            Color muted = pixel.Lerp(new Color(light, light, light, pixel.A), 0.18f);
            image.SetPixel(x, y, muted.Lerp(new Color(0.83f, 0.65f, 0.44f, pixel.A), 0.09f));
        }
        return ImageTexture.CreateFromImage(image);
    }

    private void StartRound()
    {
        retry.Hide();
        mine.Clear();
        rival.Clear();
        discarding.Clear();
        held = match = hoveredRival = targetCard = carried = destinationCard = null;
        inspecting = dragged = fromRival = false;
        move = Move.Wait;
        reachTime = -1;
        restTime = 0;
        paw = ArmRest;
        result = null;
        countdown = 3;
        ai = new OpponentAI();
        var deck = new List<Card>(20);
        for (int rank = 1; rank <= 10; rank++)
        {
            deck.Add(new Card(rank));
            deck.Add(new Card(rank, true));
        }
        for (int i = deck.Count - 1; i > 0; i--)
        {
            int j = rng.RandiRange(0, i);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
        mine.AddRange(deck.GetRange(0, 10));
        rival.AddRange(deck.GetRange(10, 10));
        rival.Insert(rng.RandiRange(0, rival.Count), new Card(0));
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
        hoveredMine = !inspecting && held == null && countdown <= 0 && result == null ? Pick(mine) : null;
        hoveredRival = hoveredMine == null && !inspecting && held == null && result == null && countdown <= 0 ? RivalAtPointer() : null;
        Layout(1 - Mathf.Exp(-18 * (float)delta));
        QueueRedraw();
    }

    private static Vector2 Slot(int i, int total, bool upper)
    {
        float t = i - (total - 1) / 2f;
        float arc = Mathf.Pow(t / Mathf.Max(1, (total - 1) / 2f), 2);
        return new((upper ? 566 : 206) + t * Mathf.Min(upper ? 21 : 28, 470f / Mathf.Max(1, total - 1)),
                   upper ? 287 + arc * 31 : 386 + arc * 49);
    }

    // The dragged rival card still belongs to the list but occupies no visible slot.
    private int RivalGapIndex
    {
        get
        {
            int hidden = held == null ? -1 : rival.IndexOf(held);
            return hidden >= 0 && hidden < insertionSlot ? insertionSlot - 1 : insertionSlot;
        }
    }

    private Card RivalGripAtPointer()
    {
        if (move is not (Move.Pair or Move.Shuffle) || reachTime < 0.32f ||
            targetCard == null || !rival.Contains(targetCard)) return null;
        Vector2 local = (pointer - targetCard.Position).Rotated(-targetCard.Angle) / RivalScale;
        return bounds.HasPoint(local) ? targetCard : null;
    }

    private Card RivalAtPointer()
    {
        if (carried != null) return Pick(rival);
        // Resting positions keep the cursor on the same card while its neighbours slide.
        for (int i = rival.Count - 1; i >= 0; i--)
        {
            float angle = (i - (rival.Count - 1) / 2f) * 0.102f;
            Vector2 local = (pointer - Slot(i, rival.Count, true)).Rotated(-angle);
            if (bounds.HasPoint(local / RivalScale)) return rival[i];
        }
        return null;
    }

    private void Layout(float weight)
    {
        foreach (bool upper in new[] { false, true })
        {
            var cards = upper ? rival : mine;
            int count = cards.Count(c => c != held);
            bool receiving = upper && carried != null;
            bool opening = receiving || (!upper && held != null && !fromRival && match == null);
            int openAt = receiving ? RivalGapIndex : gap;
            int slots = count + (opening ? 1 : 0);
            int focus = upper && hoveredRival != null ? rival.IndexOf(hoveredRival) : -1;
            if (!upper && held == null)
            {
                // Use resting positions so the opening gap cannot move its own trigger.
                for (int n = 0; n < count; n++)
                {
                    Vector2 local = pointer - Slot(n, count, false);
                    if (bounds.Grow(5).HasPoint(local / PlayerScale)) focus = n;
                }
            }
            int i = 0;
            foreach (Card card in cards)
            {
                if (card == held) continue;
                if (opening && i == openAt) i++;
                if (upper && card == targetCard && move is (Move.Pair or Move.Shuffle) && reachTime >= 0.32f)
                {
                    i++;
                    continue;
                }
                Vector2 destination = Slot(i, slots, upper);
                if (receiving)
                    destination.X += i < openAt ? -16 : 16;
                if (focus >= 0 && !receiving)
                {
                    int distance = i - focus;
                    destination.X += Mathf.Sign(distance) * Mathf.Max(0, 31 - 7 * Mathf.Abs(distance));
                    if (!upper && distance == 0) destination.Y -= 8;
                }
                card.Position = card.Position.Lerp(destination, weight);
                card.Angle = Mathf.Lerp(card.Angle, (i - (slots - 1) / 2f) * (upper ? 0.102f : 0.115f), weight);
                i++;
            }
        }
    }

    private Card Pick(List<Card> cards)
    {
        return cards.LastOrDefault(c => c != held && bounds.HasPoint((pointer - c.Position).Rotated(-c.Angle) / (cards == rival ? RivalScale : PlayerScale)));
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
        Card grip = button.Pressed && held == null ? RivalGripAtPointer() : null;
        if (button.Pressed && held == null && grip == null && GlassBounds.HasPoint(pointer))
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
            held = grip ?? Pick(mine);
            fromRival = grip != null || held == null;
            if (held == null) held = RivalAtPointer();
            if (grip != null)
            {
                targetCard = destinationCard = null;
                deposit = paw;
                reachTime = 0.67f;
            }
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
        if (!mine.Exists(c => c != held && c.Rank == held.Rank)) return null;
        foreach (var offset in SnapOffsets)
        {
            var probe = pointer + offset;
            for (int i = mine.Count - 1; i >= 0; i--)
            {
                var card = mine[i];
                if (card == held || !bounds.HasPoint((probe - card.Position).Rotated(-card.Angle) / PlayerScale)) continue;
                if (card.Rank == held.Rank) return card;
                break;
            }
        }
        return null;
    }

    private static readonly Vector2[] SnapOffsets = BuildSnapOffsets();

    private static Vector2[] BuildSnapOffsets()
    {
        var offsets = new List<Vector2> { Vector2.Zero };
        for (int radius = 5; radius <= 25; radius += 5)
        {
            for (int direction = 0; direction < 16; direction++)
                offsets.Add(Vector2.FromAngle(Mathf.Tau * direction / 16) * radius);
        }
        return offsets.ToArray();
    }

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

    private const float RivalScale = 1.35f, PlayerScale = 2.05f;

    public override void _Draw()
    {
        Layer(BackgroundArt);
        Layer(PartyShadow);
        Layer(PartyArt);
        Layer(LampArt);
        bool checking = move == Move.Inspect && reachTime >= 0 && RivalGlassArt != null;
        if (checking)
            Layer(RivalGlassArt);
        else
        {
            Layer(BodyArt);
            Layer(FaceArt);
            Layer(SleeveBase);
        }
        Layer(TableArt);
        Layer(GlassArt);
        if (!checking) DrawReachingArm();
        Card lifted = move is Move.Pair or Move.Shuffle && reachTime >= 0.32f ? targetCard : null;
        foreach (var card in rival) if (card != held && card != lifted) Paint(card, true);
        if (SupportArt != null) DrawTextureRect(SupportArt, new Rect2(517,314,117,104), false);
        if (reachTime < 0 && PalmArt != null)
            DrawTextureRect(PalmArt, new Rect2(302,183,107,158), false);
        if (PlayerSupport != null) DrawTextureRect(PlayerSupport,new Rect2(99,421,285,264),false);
        foreach (var card in mine) if (card != held) Paint(card, false);
        if (ThumbArt != null) DrawTextureRect(ThumbArt,new Rect2(127,459,160,106),false);
        foreach (var card in discarding) Paint(card, false);
        if (held != null) Paint(held, fromRival);
        if (lifted != null && lifted != held && rival.Contains(lifted)) Paint(lifted, true);
        if (carried != null) Paint(carried, false);
        if (!checking && reachTime >= 0 && RivalPawArt != null)
        {
            Vector2 size = new Vector2(92,66) * ForegroundScale(paw.Y);
            DrawTextureRect(RivalPawArt,new Rect2(paw - size / 2,size),false);
        }
        var hand = held == null ? HandArt : ClosedHandArt;
        if (hand != null)
        {
            Vector2 tip = new Rect2(0,0,960,600).HasPoint(pointer) ? pointer : new Vector2(395,410);
            DrawTextureRect(hand,new Rect2(tip - new Vector2(28,25),new Vector2(277,217)),false);
        }
        if (inspecting && result == null) DrawMagnifier();
        if (countdown > 0 || result != null)
            DrawString(ThemeDB.FallbackFont,new(0,325),result ?? Mathf.CeilToInt(countdown).ToString(),HorizontalAlignment.Center,960,52,Gold);
    }

    private void DrawMagnifier()
    {
        Vector2 lens = new(480, 300);
        Rect2 viewport = GetViewportRect();
        Transform2D toLocal = GetGlobalTransformWithCanvas().AffineInverse();
        Vector2 corner = toLocal * viewport.Position;
        DrawRect(new Rect2(corner, toLocal * viewport.End - corner).Grow(1), new Color("#171524"));
        DrawSetTransform(lens, 0, new Vector2(1.10f, 1));
        DrawCircle(Vector2.Zero, 409, new Color("#b8b0c7"), true, -1, true);
        DrawCircle(Vector2.Zero, 401, new Color("#45445f"), true, -1, true);
        DrawArc(Vector2.Zero, 389, 3.4f, 4.22f, 50, new Color("#bbb8d6"), 3, true);

        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        int count = rival.Count;
        float half = Mathf.Max(1, (count - 1) * 0.5f);
        float tableSpacing = Mathf.Min(21, 470f / Mathf.Max(1, count - 1));
        float lensSpacing = Mathf.Min(91, 640f / Mathf.Max(1, count - 1));

        // Project the animated table pose, including cards between slots.
        void DrawLiveCard(Card card)
        {
            float x = Mathf.Clamp((card.Position.X - 566) / tableSpacing * lensSpacing, -320, 320);
            float y = Mathf.Clamp(-35 + (card.Position.Y - 287) * (44f / 31), -65, 100);
            float angle = Mathf.Clamp(card.Angle / (half * 0.102f), -1, 1) * 0.19f;
            // Keep even the tilted outer corners of a moving card within the lens.
            float maxX = 441 * Mathf.Sqrt(1 - Mathf.Pow((Mathf.Abs(y) + 135) / 401f, 2)) - 101;
            x = Mathf.Clamp(x, -maxX, maxX);
            DrawSetTransform(lens + new Vector2(x, y), angle, Vector2.One * 1.85f);
            if (card.Rank == 0)
            {
                DrawRect(bounds.Grow(5), new Color("#ffcc7066"));
                DrawRect(bounds.Grow(2), Gold);
            }
            DrawRect(bounds, new Color("#d9d5e2bd"));
            Texture2D art = Face(card);
            if (art != null)
                DrawTextureRect(art, bounds, false, new Color(1, 1, 1, 0.74f));
            else
                DrawString(ThemeDB.FallbackFont, new(-38, 9), card.Rank == 0 ? "JOKER" : card.Rank.ToString(),
                    HorizontalAlignment.Center, 76, card.Rank == 0 ? 20 : 30, Ink);
            DrawRect(bounds, card.Rank == 0 ? Gold : new Color("#f2e9d7"), false, 1.5f);
        }
        Card moving = reachTime >= 0 && move != Move.Steal ? targetCard : null;
        foreach (Card card in rival)
            if (card != moving) DrawLiveCard(card);
        if (moving != null && rival.Contains(moving)) DrawLiveCard(moving);
        foreach (Card card in discarding)
            if (card.LeavingRivalHand) DrawLiveCard(card);
        if (carried != null && carried.Position.Y < 355) DrawLiveCard(carried);
        DrawSetTransform(lens + new Vector2(0, 104), 0, Vector2.One);
        if (SupportArt != null) DrawTextureRect(SupportArt, new Rect2(-87, -69, 174, 138), false);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
    private void Paint(Card card, bool hidden)
    {
        float scale = card == carried ? CarriedScale : hidden ? RivalScale : PlayerScale;
        DrawSetTransform(card.Position,card.Angle,Vector2.One * scale);

        Texture2D tex = hidden ? Back : Face(card);
        if (tex != null)
        {
            DrawTextureRect(tex, bounds, false);
            if (!hidden) DrawRect(bounds, new Color("#c29b75"), false, 0.8f);
        }
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
        bool selected = card == held || (held == null && (card == hoveredMine || card == hoveredRival));
        bool pair = held != null && !fromRival && held.Rank > 0 && mine.Contains(card) && card.Rank == held.Rank;
        if (selected || pair)
        {
            DrawRect(bounds.Grow(2), new Color("#ffb83e66"), false, 3);
            DrawRect(bounds, new Color("#fff1a032"));
            DrawRect(bounds.Grow(-1), new Color("#ffdf86"), false, 2);
        }
        DrawSetTransform(Vector2.Zero,0,Vector2.One);
    }
}
