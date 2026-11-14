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
    private Vector2 grabLocal, previousDragPointer;
    private Vector2? lastDropPointer;
    private float grabAngle;
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
    private Card landedCard;
    private float landingGlow;
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
            landedCard = null;
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
                float slotAngle = SlotAngle(RivalGapIndex, count, true);
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
                    landedCard = carried;
                    landingGlow = 1;
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
                ? RivalMoveDestination() + GripOffset(targetCard.Angle, RivalScale) : reachEnd;
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
                else
                {
                    rival.Insert(to, targetCard);
                    landedCard = targetCard;
                    landingGlow = 1;
                }
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
    private bool transitioning;

    private void CheckResult()
    {
        if (mine.Count == 1 && mine[0].Rank == 0) result = "WIN";
        if (rival.Count == 1 && rival[0].Rank == 0 && carried == null) result = "LOSE";
        if (result != null && !transitioning)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            transitioning = true;
            TransitionToResults(result == "WIN");
        }
    }

    private void TransitionToResults(bool playerWon)
    {
        var fadeLayer = new CanvasLayer { Layer = 100 };
        var fadeRect = new ColorRect
        {
            Color = new Color(0, 0, 0, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        fadeRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        fadeLayer.AddChild(fadeRect);
        AddChild(fadeLayer);

        Tween tween = CreateTween();
        tween.TweenInterval(0.6);
        tween.TweenProperty(fadeRect, "color:a", 1.0f, 0.6)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
        tween.TweenCallback(Callable.From(() => LoadResultsScreen(playerWon)));
    }

    private void LoadResultsScreen(bool playerWon)
    {
        PackedScene resultsPacked = GD.Load<PackedScene>("res://Scenes/Results/results_screen.tscn");
        ResultsScreen resultsScreen = resultsPacked.Instantiate<ResultsScreen>();
        resultsScreen.PlayerWon = playerWon;

        SceneTree tree = GetTree();
        Node currentScene = tree.CurrentScene;
        tree.Root.AddChild(resultsScreen);
        tree.CurrentScene = resultsScreen;
        currentScene?.QueueFree();
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
        Input.MouseMode = Input.MouseModeEnum.Hidden;
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

    public override void _ExitTree()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _Process(double delta)
    {
        Fit();
        pointer = GetLocalMousePosition();
        landingGlow = Mathf.MoveToward(landingGlow, 0, (float)delta / 0.45f);
        countdown = Mathf.Max(0, countdown - (float)delta);
        if (held != null)
        {
            Aim();
            float dt = Mathf.Max((float)delta, 0.001f);
            float speedX = (pointer.X - previousDragPointer.X) / dt;
            float tilt = Mathf.Clamp(speedX * 0.00016f, -0.14f, 0.14f);
            float weight = 1 - Mathf.Exp(-32 * dt);
            float dragAngle = !fromRival && dragged && pointer.Y >= 355
                ? SlotAngle(gap, mine.Count, false) : grabAngle + tilt;
            held.Angle = Mathf.LerpAngle(held.Angle, match?.Angle ?? dragAngle, weight);
            Vector2 destination = match == null ? pointer - grabLocal.Rotated(held.Angle)
                : match.Position + new Vector2(5, -5);
            held.Position = fromRival ? held.Position.Lerp(destination, weight) : destination;
            previousDragPointer = pointer;
        }
        UpdateArm((float)delta);
        bool canHover = !inspecting && held == null && countdown <= 0 && result == null;
        Card grip = canHover ? RivalGripAtPointer() : null;
        if (lastDropPointer.HasValue && pointer.DistanceSquaredTo(lastDropPointer.Value) > 9)
            lastDropPointer = null;
        hoveredMine = canHover && !lastDropPointer.HasValue && grip == null && !GlassBounds.HasPoint(pointer) ? Pick(mine) : null;
        hoveredRival = grip ?? (canHover && hoveredMine == null && !GlassBounds.HasPoint(pointer) ? Pick(rival) : null);
        Layout(1 - Mathf.Exp(-18 * (float)delta));
        QueueRedraw();
    }

    private const float PlayerFanRadius = 298, RivalFanRadius = 251;
    private const float RivalFanCenterX = 612;

    private static float FanStep(int total, bool upper) =>
        Mathf.Min(upper ? 0.116f : 0.124f, 1.37f / Mathf.Max(1, total - 1));

    private static float SlotAngle(int i, int total, bool upper) =>
        (i - (total - 1) * 0.5f) * FanStep(total, upper);

    private static Vector2 Slot(int i, int total, bool upper) =>
        FanPosition(SlotAngle(i, total, upper), total, upper);

    private static Vector2 FanPosition(float angle, int total, bool upper)
    {
        float radius = upper ? RivalFanRadius : PlayerFanRadius;
        float centerX = RivalFanCenterX;
        if (!upper)
        {
            // Leave room for the tilted outer card and its top-left rank.
            float outer = Mathf.Abs(SlotAngle(0, total, false));
            float leftExtent = (radius + 60 * PlayerScale) * Mathf.Sin(outer)
                + 38 * PlayerScale * Mathf.Cos(outer);
            centerX = Mathf.Max(206, leftExtent + 8);
        }
        return new(centerX + radius * Mathf.Sin(angle),
            (upper ? 287 : 386) + radius * (1 - Mathf.Cos(angle)));
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

    private const float HoverLift = 14;

    private void Layout(float weight)
    {
        foreach (bool upper in new[] { false, true })
        {
            var cards = upper ? rival : mine;
            int count = cards.Count(c => c != held);
            bool receiving = upper && carried != null;
            bool shuffling = upper && move == Move.Shuffle && reachTime >= 0.32f &&
                targetCard != null && targetCard != held && destinationCard != held && rival.Contains(destinationCard);
            bool opening = receiving || shuffling || (!upper && held != null && !fromRival && match == null);
            int openAt = receiving ? RivalGapIndex : shuffling ? RivalShuffleIndex() : gap;
            int slots = count + (opening && !shuffling ? 1 : 0);
            int landing = receiving || shuffling ? openAt : -1;
            if (upper && landing < 0 && landingGlow > 0 && landedCard != null && landedCard != held)
            {
                landing = rival.IndexOf(landedCard);
                int hidden = rival.IndexOf(held);
                if (hidden >= 0 && hidden < landing) landing--;
            }
            int focus = held == null ? cards.IndexOf(upper ? hoveredRival : hoveredMine) : -1;
            int i = 0;
            foreach (Card card in cards)
            {
                if (card == held) continue;
                if (shuffling && card == targetCard) continue;
                if (opening && i == openAt) i++;
                if (upper && card == targetCard && move is (Move.Pair or Move.Shuffle) && reachTime >= 0.32f)
                {
                    i++;
                    continue;
                }
                float angle = SlotAngle(i, slots, upper);
                if (!upper && opening && dragged && pointer.Y >= 355)
                {
                    int distance = i - openAt;
                    float spread = Mathf.Max(0, 36 - 9 * Mathf.Abs(distance));
                    angle += Mathf.Sign(distance) * spread / PlayerFanRadius;
                }
                else if (landing >= 0)
                {
                    int distance = i - landing;
                    float strength = receiving || shuffling ? 1 : landingGlow;
                    float spread = Mathf.Max(0, 43 - 10 * Mathf.Abs(distance)) * strength;
                    angle += Mathf.Sign(distance) * spread / RivalFanRadius;
                }
                else if (focus >= 0)
                {
                    int distance = i - focus;
                    float arcLength = Mathf.Sign(distance) * Mathf.Max(0, 31 - 7 * Mathf.Abs(distance));
                    angle += arcLength / (upper ? RivalFanRadius : PlayerFanRadius);
                }
                Vector2 offset = card.Position - FanPosition(card.Angle, slots, upper);
                Vector2 targetOffset = i == focus ? Vector2.Up * HoverLift : Vector2.Zero;
                card.Angle = Mathf.LerpAngle(card.Angle, angle, weight);
                card.Position = FanPosition(card.Angle, slots, upper) + offset.Lerp(targetOffset, weight);
                i++;
            }
        }
    }

    private Card Pick(List<Card> cards)
    {
        bool upper = cards == rival;
        float scale = upper ? RivalScale : PlayerScale;
        Card previous = upper ? hoveredRival : hoveredMine;
        for (int i = cards.Count - 1; i >= 0; i--)
        {
            Card card = cards[i];
            if (card == held) continue;
            Vector2 offset = pointer - card.Position;
            if (bounds.HasPoint(offset.Rotated(-card.Angle) / scale)) return card;
            // Keep the original hit area while the hovered card lifts off the pointer.
            if (card == previous && bounds.HasPoint((offset - Vector2.Down * HoverLift).Rotated(-card.Angle) / scale))
                return card;
        }
        return null;
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
            lastDropPointer = null;
            dragged = false;
            held = grip ?? Pick(mine);
            fromRival = grip != null || held == null;
            if (held == null) held = Pick(rival);
            if (grip != null)
            {
                targetCard = destinationCard = null;
                deposit = paw;
                reachTime = 0.67f;
            }
            hoveredRival = null;
            if (held != null)
            {
                grabAngle = held.Angle;
                grabLocal = (pointer - held.Position).Rotated(-grabAngle);
                previousDragPointer = pointer;
                Aim();
            }
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
                held.Angle = SlotAngle(gap, mine.Count, false);
                held.Position = Slot(gap, mine.Count, false);
                lastDropPointer = pointer;
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
        if (!fromRival)
        {
            // Use the same fan and grab point for the preview and the final placement.
            gap = 0;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < mine.Count; i++)
            {
                float gripX = Slot(i, mine.Count, false).X + grabLocal.Rotated(SlotAngle(i, mine.Count, false)).X;
                float distance = Mathf.Abs(pointer.X - gripX);
                if (distance >= nearest) continue;
                nearest = distance;
                gap = i;
            }
        }
        dragged |= pointer.DistanceSquaredTo(pressPoint) >= 36;
        match = dragged && !fromRival && held.Rank > 0 ? NearbyMatch() : null;
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
        for (int radius = 5; radius <= 35; radius += 5)
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

    private Vector2 RivalMoveDestination()
    {
        if (move == Move.Pair) return destinationCard.Position;
        return Slot(RivalShuffleIndex(), rival.Count - (rival.Contains(held) ? 1 : 0), true);
    }

    private int RivalShuffleIndex()
    {
        int index = rival.IndexOf(destinationCard);
        int hidden = rival.IndexOf(held);
        if (hidden < 0) return index;
        if (rival.IndexOf(targetCard) < hidden) hidden--;
        return hidden < index ? index - 1 : index;
    }

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
        if (SupportArt != null) DrawTextureRect(SupportArt, new Rect2(533,314,117,104), false);
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
        if (hand != null && result == null)
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
        float step = FanStep(count, true);
        float lensSpacing = Mathf.Min(91, 640f / Mathf.Max(1, count - 1));

        // Project the animated table pose, including cards between slots.
        void DrawLiveCard(Card card)
        {
            float tableAngle = Mathf.Asin(Mathf.Clamp((card.Position.X - RivalFanCenterX) / RivalFanRadius, -1, 1));
            float x = Mathf.Clamp(tableAngle / step * lensSpacing, -320, 320);
            float y = Mathf.Clamp(-35 + (card.Position.Y - 287) * (44f / 31), -65, 100);
            float angle = Mathf.Clamp(card.Angle / (half * step), -1, 1) * 0.19f;
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
        bool snapped = card == held && match != null;
        Vector2 previewOffset = snapped ? new Vector2(12, -18).Rotated(card.Angle) : Vector2.Zero;
        DrawSetTransform(card.Position + previewOffset,card.Angle,Vector2.One * scale);

        Texture2D tex = hidden ? Back : Face(card);
        if (tex != null)
        {
            bool translucent = snapped || discarding.Contains(card);
            DrawTextureRect(tex, bounds, false, new Color(1, 1, 1, translucent ? 0.55f : 1));
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
        if (hidden && card == landedCard && landingGlow > 0)
        {
            DrawRect(bounds, new Color(1, 0.85f, 0.46f, 0.34f * landingGlow));
            DrawRect(bounds, new Color(1, 0.92f, 0.65f, landingGlow), false, 2);
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
