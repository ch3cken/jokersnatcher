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
    [Export] public Texture2D GlassIcon {get; set;}
    [Export] public Texture2D MagnifiedBackground { get; set; }
    [Export] public Texture2D MagnifiedFrame { get; set; }
    [Export] public Texture2D BackgroundArt { get; set; }
    [Export] public Texture2D PartyArt { get; set; }
    [Export] public Texture2D PartyShadow { get; set; }
    [Export] public Texture2D LampArt { get; set; }
    [Export] public Texture2D FaceArt { get; set; }
    [Export] public Texture2D SelfFaceArt { get; set; }
    [Export] public Texture2D StressFaceArt { get; set; }
    [Export] public Texture2D SmileFaceArt { get; set; }
    [Export] public Texture2D GrimaceFaceArt { get; set; }
    [Export] public Texture2D SelfStressFaceArt { get; set; }
    [Export] public Texture2D SleeveBase { get; set; }
    [Export] public Texture2D SupportArt { get; set; }
    [Export] public Texture2D ClosedHandArt { get; set; }
    [Export] public Texture2D PlayerSupport { get; set; }
    [Export] public Texture2D ThumbArt { get; set; }
    [Export] public Texture2D[] SecondFaces { get; set; } = new Texture2D[11];
    [Export] public Texture2D RivalPawArt { get; set; }
    [Export] public Texture2D Count3Art { get; set; }
    [Export] public Texture2D Count2Art { get; set; }
    [Export] public Texture2D Count1Art { get; set; }
    [Export] public Texture2D GoArt { get; set; }
    [Export] public Texture2D WinArt { get; set; }
    [Export] public Texture2D LoseArt { get; set; }

    private sealed class Card(int rank, bool second = false)
    {
        public readonly int Rank = rank;
        public readonly bool Second = second;
        public Vector2 Position;
        public float Angle;
        public bool LeavingRivalHand;
        public float DrawnScale = PlayerScale;
        public float Alpha = 1f;
    }

    private readonly List<Card> mine = new();
    private readonly List<Card> rival = new();
    private readonly Rect2 bounds = new(-38, -60, 76, 120);
    private readonly RandomNumberGenerator rng = new();
    private readonly List<Card> discarding = new();
    private Card held, hoveredRival, hoveredMine;
    private bool fromRival, inspecting;
    private float inspectProgress, handOpacity = 1;
    private float idleTime;
    private Vector2 playerSway, rivalSway;
    private bool overLensCards;
    private int playerInspectUses;
    private Texture2D glassSilhouette;
    private static readonly Rect2 GlassBounds = new(647, 455, 251, 143);
    private static readonly Vector2 InspectBadgeCenter = new(867, 466);
    private const float InspectBadgeWidth = 94f;
    private const int InspectBadgeFontSize = 36;
    private Font uiFont;
    private Vector2 pressPoint;
    private float grabAngleOffset;
    private Vector2? lastDropPointer;
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
    private int animationSlot;
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
            if (move == Move.Inspect)
                SoundEffectManager.Instance?.PlayEnemyMagnifier();
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
                    SoundEffectManager.Instance?.PlayCard();
                    if (held == carried) held = null;
                    if (held == null || carried == pendingPair) pendingPair = null;
                }
                targetCard = null;
                deposit = ArmRest;
                if (carried != null) animationSlot = rng.RandiRange(0, rival.Count);
            }
            if (carried != null)
            {
                // The player can draw from this hand while the paw is returning.
                animationSlot = Mathf.Min(animationSlot, rival.Count);
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
                    // Draw independently of the paw animation, using the hand size at release.
                    int finalSlot = rng.RandiRange(0, rival.Count);
                    rival.Insert(finalSlot, carried);
                    int hidden = rival.IndexOf(held);
                    int visibleSlot = hidden >= 0 && hidden < finalSlot ? finalSlot - 1 : finalSlot;
                    int visibleCount = rival.Count - (hidden >= 0 ? 1 : 0);
                    carried.Position = Slot(visibleSlot, visibleCount, true);
                    carried.Angle = SlotAngle(visibleSlot, visibleCount, true);
                    landedCard = carried;
                    landingGlow = 1;
                    carried = null;
                    SoundEffectManager.Instance?.PlayCard();
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
                    ai.GainInspectUse();
                    CheckResult();
                }
                else
                {
                    rival.Insert(to, targetCard);
                    landedCard = targetCard;
                    landingGlow = 1;
                    SoundEffectManager.Instance?.PlayCard();
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
    [Export] public double ResultsDelaySeconds { get; set; } = 2;
    [Export] public double ResultsFadeOutSeconds { get; set; } = 1;
    private const int FadeOverlayLayer = 100;
    private bool transitioning;

    private void CheckResult()
    {
        if (mine.Count == 1 && mine[0].Rank == 0) result = "WIN";
        if (rival.Count == 1 && rival[0].Rank == 0 && carried == null) result = "LOSE";
        if (result != null && !transitioning)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            transitioning = true;
            if (result == "WIN")
                SoundEffectManager.Instance?.PlayVictory();
            else
                SoundEffectManager.Instance?.PlayDefeat();
            TransitionToResults(result == "WIN");
        }
    }

    private void TransitionToResults(bool playerWon)
    {
        var fadeLayer = new CanvasLayer { Layer = FadeOverlayLayer };
        var fadeRect = new ColorRect
        {
            Color = new Color(0, 0, 0, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        fadeRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        fadeLayer.AddChild(fadeRect);
        AddChild(fadeLayer);

        Tween tween = CreateTween();
        tween.TweenInterval(ResultsDelaySeconds);
        tween.TweenProperty(fadeRect, "color:a", 1.0f, ResultsFadeOutSeconds)
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
    private float goTime;
    private int shownCountdown = -1;
    private const float CountdownSeconds = 3f, DealInterval = 0.045f, DealFlight = 0.36f;
    private float dealDuration;
    private string result;
    private static readonly Color Ink = new("#242238"), Paper = new("#fff0cf"), Gold = new("#ffcd70");

    public override void _Ready()
    {
        if (GlassArt != null)
        {
            using Image mask = GlassArt.GetImage();
            if (mask.IsCompressed()) mask.Decompress();
            mask.Convert(Image.Format.Rgba8);
            Rect2I area = mask.GetUsedRect();
            for (int y = area.Position.Y; y < area.End.Y; y++)
            for (int x = area.Position.X; x < area.End.X; x++)
                mask.SetPixel(x, y, new Color(1, 1, 1, mask.GetPixel(x, y).A));
            glassSilhouette = ImageTexture.CreateFromImage(mask);
        }
        TintFaces(Faces, new Color("#58504b"));
        TintFaces(SecondFaces, new Color("#ac6257"));
        if (Back != null) Back = WarmCardBack(Back);
        AddChild(retry);
        uiFont = GD.Load<Font>("res://Art/Fonts/Galmuri/Galmuri11.ttf");
        retry.AddThemeFontOverride("font", GD.Load<Font>("res://Art/Fonts/Galmuri/Galmuri11-Bold.ttf"));
        retry.AddThemeFontSizeOverride("font_size", 24);
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
        held = hoveredRival = targetCard = carried = destinationCard = null;
        inspecting = dragged = fromRival = false;
        inspectProgress = 0;
        handOpacity = 1;
        playerInspectUses = 0;
        move = Move.Wait;
        reachTime = -1;
        restTime = 0;
        paw = ArmRest;
        result = null;
        shownCountdown = -1;
        goTime = 0;
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
        dealDuration = (mine.Count + rival.Count - 1) * DealInterval + DealFlight;
        countdown = CountdownSeconds + dealDuration;
        Fit();
        Layout(1);
        SoundEffectManager.Instance?.PlayDeal();
    }

    private void Fit()
    {
        Vector2 view = GetViewportRect().Size;
        Vector2 titleSize = new(1152, 648);
        float windowScale = Mathf.Min(view.X / titleSize.X, view.Y / titleSize.Y);
        // Match TitleScreen's Keep Aspect Covered (360x220 -> 1152x704 centered in 1152x648).
        Vector2 cover = new Vector2(titleSize.X, titleSize.X * 220f / 360f) * windowScale;
        Scale = cover / new Vector2(960, 600);
        Position = (view - cover) / 2;
    }

    public override void _ExitTree()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _Process(double delta)
    {
        Fit();
        pointer = GetLocalMousePosition();
        idleTime += (float)delta;
        playerSway = new Vector2(4f * Mathf.Sin(idleTime * 1.45f), 8f * Mathf.Sin(idleTime * 1.85f));
        rivalSway = new Vector2(4f * Mathf.Sin(idleTime * 1.32f + 1), 8f * Mathf.Sin(idleTime * 1.63f + 2));
        inspectProgress = Mathf.MoveToward(inspectProgress, inspecting && result == null ? 1 : 0, (float)delta / 0.25f);
        bool overCards = inspectProgress > 0.5f ? overLensCards :
            PointerOverHand(mine, PlayerScale) || PointerOverHand(rival, RivalScale);
        handOpacity = Mathf.Lerp(handOpacity, overCards ? 0.48f : 1, 1 - Mathf.Exp(-24 * (float)delta));
        landingGlow = Mathf.MoveToward(landingGlow, 0, (float)delta / 0.45f);
        bool counting = countdown > 0;
        countdown = Mathf.Max(0, countdown - (float)delta);
        if (countdown > 0 && countdown <= CountdownSeconds)
        {
            int tick = Mathf.CeilToInt(countdown);
            if (tick != shownCountdown)
            {
                shownCountdown = tick;
                SoundEffectManager.Instance?.PlayCountdown();
            }
        }
        else if (counting && countdown == 0)
        {
            shownCountdown = 0;
            goTime = 1f;
            SoundEffectManager.Instance?.PlayGo();
        }
        else
            goTime = Mathf.Max(0, goTime - (float)delta);
        if (held != null)
        {
            Aim();
        }
        UpdateArm((float)delta);
        rivalSway.Y += RivalInspectBounce;
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
    private const float MaxPlayerGapSpread = 27f;
    private const float HeldLift = HoverLift + 24f;

    [Export] public float PlayerHandOffsetX { get; set; } = 0f;
    [Export] public float PlayerHandOffsetY { get; set; } = 0f;
    [Export] public float PlayerCardOffsetX { get; set; } = 16f;
    [Export] public float PlayerCardOffsetY { get; set; } = 0f;
    [Export(PropertyHint.Range, "-90,90,0.5")]
    public float PlayerFanRotationDegrees { get; set; } = 0f;

    private float BaseFanAngle(bool upper) => upper ? 0f : Mathf.DegToRad(PlayerFanRotationDegrees);

    private static float SpreadScale(int slots) =>
        1f + 0.65f * Mathf.Clamp((slots - 5) / 5f, 0f, 1f);

    private static float FanStep(int total, bool upper)
    {
        int gaps = Mathf.Max(1, total - 1);
        // Keep the opening deal's spacing, then widen each gap gradually as cards leave.
        float openingGaps = upper ? 10 : 9;
        float openingStep = upper ? 0.116f : 0.124f;
        return Mathf.Min(openingStep * Mathf.Sqrt(openingGaps / gaps), 1.37f / gaps);
    }

    private static float HalfFanSpread(int total, bool upper) =>
        (total - 1) * 0.5f * FanStep(total, upper);

    private float SlotAngle(int i, int total, bool upper) =>
        BaseFanAngle(upper) + (i - (total - 1) * 0.5f) * FanStep(total, upper);

    private Vector2 Slot(int i, int total, bool upper) =>
        FanPosition(SlotAngle(i, total, upper), total, upper);

    private Vector2 FanPosition(float angle, int total, bool upper)
        => FanCenter(total, upper) + Vector2.Up.Rotated(angle) * (upper ? RivalFanRadius : PlayerFanRadius);

    private Vector2 FanCenter(int total, bool upper)
    {
        float radius = upper ? RivalFanRadius : PlayerFanRadius;
        float centerX = RivalFanCenterX;
        if (!upper)
        {
            // Frame the widest interaction pose, so opening a gap never pans the hand.
            float outer = HalfFanSpread(total, false);
            float openAngle = outer + MaxPlayerGapSpread * SpreadScale(total) / radius;
            float CornerExtent(float angle, float lift) =>
                (radius + lift + 60 * PlayerScale) * Mathf.Sin(angle) + 38 * PlayerScale * Mathf.Cos(angle);
            float extent = Mathf.Max(CornerExtent(openAngle, 0), CornerExtent(outer, HeldLift));
            centerX = Mathf.Max(206 - PlayerCardOffsetX, extent) + PlayerCardOffsetX + PlayerHandOffsetX; // Includes the hand sway and selection outline.
        }
        return new(centerX, (upper ? 287 : 386 + PlayerCardOffsetY + PlayerHandOffsetY) + radius);
    }

    // The dragged rival card still belongs to the list but occupies no visible slot.
    private int RivalGapIndex
    {
        get
        {
            int hidden = held == null ? -1 : rival.IndexOf(held);
            return hidden >= 0 && hidden < animationSlot ? animationSlot - 1 : animationSlot;
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
            bool opening = receiving || shuffling || (!upper && held != null && !fromRival);
            int openAt = receiving ? RivalGapIndex : shuffling ? RivalShuffleIndex() : gap;
            int slots = count + (opening && !shuffling ? 1 : 0);
            float spreadScale = SpreadScale(slots);
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
                if (!upper && card == pendingPair)
                {
                    i++;
                    continue;
                }
                if (upper && card == targetCard && move is (Move.Pair or Move.Shuffle) && reachTime >= 0.32f)
                {
                    i++;
                    continue;
                }
                float angle = SlotAngle(i, slots, upper);
                if (!upper && opening && dragged)
                {
                    int distance = i - openAt;
                    float spread = Mathf.Max(0, MaxPlayerGapSpread - 9 * (Mathf.Abs(distance) - 1));
                    angle += Mathf.Sign(distance) * spread * spreadScale / PlayerFanRadius;
                }
                else if (landing >= 0)
                {
                    int distance = i - landing;
                    float strength = receiving || shuffling ? 1 : landingGlow;
                    float spread = Mathf.Max(0, 43 - 10 * Mathf.Abs(distance)) * strength;
                    angle += Mathf.Sign(distance) * spread * spreadScale / RivalFanRadius;
                }
                else if (focus >= 0)
                {
                    int distance = i - focus;
                    float arcLength = Mathf.Sign(distance) * Mathf.Max(0, 31 - 7 * Mathf.Abs(distance));
                    angle += arcLength * spreadScale / (upper ? RivalFanRadius : PlayerFanRadius);
                }
                Vector2 offset = card.Position - FanPosition(card.Angle, slots, upper);
                Vector2 targetOffset = i == focus ? Vector2.Up.Rotated(angle) * HoverLift : Vector2.Zero;
                card.Angle = Mathf.LerpAngle(card.Angle, angle, weight);
                card.Position = FanPosition(card.Angle, slots, upper) + offset.Lerp(targetOffset, weight);
                i++;
            }
        }
    }

    private bool PointerOverHand(List<Card> cards, float scale)
    {
        foreach (Card card in cards)
        {
            bool moving = card == held || (cards == rival && card == targetCard &&
                move is (Move.Pair or Move.Shuffle) && reachTime >= 0.32f);
            Vector2 sway = moving ? Vector2.Zero : cards == rival ? rivalSway : playerSway;
            if (bounds.HasPoint((pointer - card.Position - sway).Rotated(-card.Angle) / scale)) return true;
        }
        return false;
    }

    private Card Pick(List<Card> cards)
    {
        bool upper = cards == rival;
        bool receiving = upper && carried != null;
        int slots = cards.Count + (receiving ? 1 : 0);
        if (cards.Count == 0) return null;
        Vector2 radial = pointer - (upper ? rivalSway : playerSway) - FanCenter(slots, upper);
        float radius = upper ? RivalFanRadius : PlayerFanRadius;
        float halfHeight = -bounds.Position.Y * (upper ? RivalScale : PlayerScale);
        if (radial.Length() < radius - halfHeight || radial.Length() > radius + halfHeight + HoverLift)
            return null;
        float localAngle = Vector2.Up.AngleTo(radial) - BaseFanAngle(upper);
        int index = Mathf.FloorToInt(localAngle / FanStep(slots, upper) + slots * 0.5f);
        if (index < 0 || index >= slots) return null;
        if (receiving && index == RivalGapIndex) return null;
        if (receiving && index > RivalGapIndex) index--;
        if (upper && move == Move.Shuffle && reachTime >= 0.32f && rival.Contains(targetCard) && rival.Contains(destinationCard))
        {
            int open = RivalShuffleIndex();
            if (index == open) return null;
            if (index > open) index--;
            if (index >= rival.IndexOf(targetCard)) index++;
        }
        return cards[index];
    }

    public override void _Input(InputEvent input)
    {
        if (input is not InputEventMouseButton button ||
            button.ButtonIndex != MouseButton.Left) return;
        pointer = GetGlobalTransformWithCanvas().AffineInverse() * button.Position;
        if (inspecting)
        {
            if (!button.Pressed)
            {
                inspecting = false;
                SoundEffectManager.Instance?.PlayMagnifierEnd();
            }
            QueueRedraw();
            return;
        }
        if (countdown > 0 || result != null) return;
        Card grip = button.Pressed && held == null ? RivalGripAtPointer() : null;
        if (button.Pressed && held == null && grip == null && playerInspectUses > 0 && GlassBounds.HasPoint(pointer))
        {
            playerInspectUses--;
            inspecting = true;
            SoundEffectManager.Instance?.PlayMagnifierStart();
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
                Aim();
                SoundEffectManager.Instance?.PlayCard();
            }
        }
        else if (!button.Pressed && held != null)
        {
            Aim();
            if (fromRival)
            {
                if (pointer.DistanceSquaredTo(pressPoint) >= RivalDrawDistance * RivalDrawDistance)
                {
                    rival.Remove(held);
                    int slot = rng.RandiRange(0, mine.Count);
                    ai.ObserveInsertion(slot, held.Rank == 0);
                    mine.Insert(slot, held);
                    SoundEffectManager.Instance?.PlayCard();
                    if (held.Rank == 0)
                        SoundEffectManager.Instance?.PlayJoker();
                }
            }
            else if (pendingPair != null)
            {
                Discard(pendingPair);
                Discard(held);
                playerInspectUses++;
            }
            else if (dragged)
            {
                ai.ObserveReorder(mine.IndexOf(held), gap, Difficulty);
                mine.Remove(held);
                mine.Insert(gap, held);
                held.Angle = SlotAngle(gap, mine.Count, false);
                held.Position = Slot(gap, mine.Count, false);
                lastDropPointer = pointer;
                SoundEffectManager.Instance?.PlayCard();
            }
            held = pendingPair = null;
            CheckResult();
        }
        QueueRedraw();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut)
        {
            if (inspecting)
                SoundEffectManager.Instance?.PlayMagnifierEnd();
            inspecting = false;
            held = pendingPair = null;
        }
    }

    private Card pendingPair;
    private const float RivalDrawDistance = 75;
    private float RivalDrawProgress => Mathf.SmoothStep(0, 1, Mathf.Clamp(pointer.DistanceTo(pressPoint) / RivalDrawDistance, 0, 1));

    private void Aim()
    {
        pendingPair = null;
        dragged |= pointer.DistanceSquaredTo(pressPoint) >= 36;
        int count = fromRival ? rival.Count : mine.Count;
        Vector2 center = FanCenter(count, fromRival);
        float radius = fromRival ? RivalFanRadius : PlayerFanRadius;
        float outer = HalfFanSpread(count, fromRival);
        float baseAngle = BaseFanAngle(fromRival);
        if (fromRival)
        {
            // Move the supporting arc between hands while the stolen card is in transit.
            float travel = RivalDrawProgress;
            center = center.Lerp(FanCenter(mine.Count + 1, false), travel);
            radius = Mathf.Lerp(radius, PlayerFanRadius, travel);
            outer = Mathf.Lerp(outer, HalfFanSpread(mine.Count + 1, false), travel);
            baseAngle = Mathf.Lerp(baseAngle, BaseFanAngle(false), travel);
        }
        float localAngle = Mathf.Clamp(Vector2.Up.AngleTo(pointer - center) + grabAngleOffset - baseAngle, -outer, outer);
        float angle = baseAngle + localAngle;
        held.Angle = angle;
        held.Position = center + Vector2.Up.Rotated(angle) * (radius + HeldLift);
        if (fromRival) return;

        int source = mine.IndexOf(held);
        int nextGap = Mathf.Clamp(Mathf.FloorToInt(localAngle / FanStep(count, false) + count * 0.5f), 0, count - 1);
        gap = dragged ? nextGap : source;
        if (held.Rank == 0) return;
        float fanIndex = localAngle / FanStep(count, false) + (count - 1) * 0.5f;
        float nearest = float.PositiveInfinity;
        for (int index = 0; index < count; index++)
        {
            if (index == source || mine[index].Rank != held.Rank) continue;
            int remainingIndex = index < source ? index : index - 1;
            // A click pairs only immediate neighbours; dragging allows a margin around their gaps.
            float distance = Mathf.Min(Mathf.Abs(fanIndex - remainingIndex), Mathf.Abs(fanIndex - remainingIndex - 1));
            if (!dragged && Mathf.Abs(index - source) != 1) continue;
            if (dragged && distance > 0.95f || distance >= nearest) continue;
            nearest = distance;
            pendingPair = mine[index];
        }
    }

    private void Discard(Card card)
    {
        ai.ObserveRemoval(mine.IndexOf(card));
        mine.Remove(card);
        FlyToCenter(card);
    }

    private void FlyToCenter(Card card)
    {
        SoundEffectManager.Instance?.PlayTakeCard();
        card.DrawnScale = card.LeavingRivalHand ? RivalScale : PlayerScale;
        card.Alpha = 1f;
        discarding.Add(card);
        float fromScale = card.DrawnScale;
        Vector2 discardTarget = new(480, 450);
        var tween = CreateTween();
        tween.TweenMethod(Callable.From<Vector2>(p => card.Position = p), card.Position, discardTarget, 0.32)
            .SetTrans(Tween.TransitionType.Expo)
            .SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenMethod(Callable.From<float>(s => card.DrawnScale = s), fromScale, RivalScale, 0.32)
            .SetTrans(Tween.TransitionType.Expo)
            .SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenMethod(Callable.From<float>(a => card.Alpha = a), 1f, 0f, 0.32);
        tween.Chain().TweenCallback(Callable.From(() => discarding.Remove(card)));
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

    private float RivalInspectBounce => move == Move.Inspect && reachTime >= 0
        ? -16f * Mathf.Exp(-6f * reachTime) * Mathf.Sin(20f * reachTime) : 0;

    public override void _Draw()
    {
        Layer(BackgroundArt);
        Layer(PartyShadow);
        Layer(PartyArt);
        Layer(LampArt);
        bool checking = result == null && move == Move.Inspect && reachTime >= 0 && RivalGlassArt != null;
        if (checking)
        {
            DrawSetTransform(new Vector2(0, RivalInspectBounce), 0, Vector2.One);
            Layer(RivalGlassArt);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
        else
        {
            Layer(BodyArt);
            bool lookingAtHand = reachTime >= 0 && (move is Move.Pair or Move.Shuffle
                ? targetCard != null && targetCard != held && rival.Contains(targetCard)
                : move == Move.Steal && carried != null && reachTime >= ReturnStart);
            const int lowHandSize = 3;
            bool stressed = rival.Count + (carried == null ? 0 : 1) <= lowHandSize
                || mine.Exists(static card => card.Rank == 0);
            Texture2D face = result switch
            {
                "LOSE" => SmileFaceArt,
                "WIN" => GrimaceFaceArt,
                _ when stressed => lookingAtHand ? SelfStressFaceArt : StressFaceArt,
                _ => lookingAtHand ? SelfFaceArt : FaceArt
            };
            Layer(face ?? FaceArt);
            Layer(SleeveBase);
        }
        Layer(TableArt);
        float lift = Mathf.SmoothStep(0, 1, inspectProgress);
        DrawSetTransform(Vector2.Up * (18 * lift), 0, Vector2.One);
        if (playerInspectUses > 0 && glassSilhouette != null)
        {
            bool hovered = held == null && !inspecting && result == null && countdown <= 0 &&
                GlassBounds.HasPoint(pointer) && RivalGripAtPointer() == null;
            float width = hovered ? 8 : 4;
            DrawGlassRim(width + 2, new Color("#694322"));
            DrawGlassRim(width, hovered ? new Color("#fff3a1") : new Color("#ffbd38"));
        }
        Layer(GlassArt);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        Layer(GlassIcon);
        DrawInspectUsesCount();
        if (!checking) DrawReachingArm();
        Card lifted = move is Move.Pair or Move.Shuffle && reachTime >= 0.32f ? targetCard : null;
        bool heldInFront = held != null && fromRival && RivalDrawProgress < 0.5f;
        DrawFan(rival, true, rivalSway, lifted);
        DrawSetTransform(rivalSway, 0, Vector2.One);
        if (SupportArt != null) DrawTextureRect(SupportArt, new Rect2(533,314,117,104), false);
        if (reachTime < 0 && PalmArt != null)
            DrawTextureRect(PalmArt, new Rect2(302,183,107,158), false);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        if (lifted != null && lifted != held && rival.Contains(lifted)) Paint(lifted, true);
        Vector2 playerHandTransform = playerSway + new Vector2(PlayerHandOffsetX, PlayerHandOffsetY);
        if (PlayerSupport != null)
        {
            DrawSetTransform(playerHandTransform, 0, Vector2.One);
            DrawTextureRect(PlayerSupport, new Rect2(99, 421, 285, 264), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
        if (held != null && !heldInFront) Paint(held, fromRival);
        DrawFan(mine, false, playerSway);
        foreach (var card in discarding) Paint(card, false);
        if (carried != null) Paint(carried, false);
        if (!checking && reachTime >= 0 && RivalPawArt != null)
        {
            float reachDepth = move == Move.Steal
                ? Phase(reachTime, 0, CatchTime) * (1 - Phase(reachTime, ReturnStart, DepositTime))
                : 0;
            Vector2 size = new Vector2(92,66) * ForegroundScale(paw.Y) * (1 + 0.15f * reachDepth);
            DrawTextureRect(RivalPawArt,new Rect2(paw - size / 2,size),false);
        }
        if (heldInFront) Paint(held, true);
        if (ThumbArt != null)
        {
            DrawSetTransform(playerHandTransform, 0, Vector2.One);
            DrawTextureRect(ThumbArt, new Rect2(160, 459, 160, 106), false);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
        overLensCards = false;
        float lensOpacity = Mathf.SmoothStep(0, 1, Mathf.Clamp((inspectProgress - 0.2f) / 0.8f, 0, 1));
        if (lensOpacity > 0) DrawMagnifier(lensOpacity);
        DrawCenterBanner();
        var hand = held == null ? HandArt : ClosedHandArt;
        if (hand != null && result == null)
        {
            Vector2 handSize = new Vector2(277, 217) * (ForegroundScale(pointer.Y) / ForegroundScale(505));
            // Fingertip on the leading (upper-left) digit, in cropped sprite pixels.
            Vector2 fingertip = held == null ? new Vector2(23, 2) : new Vector2(16, 4);
            Vector2 hotspot = (fingertip + new Vector2(0.5f, 0.5f)) * handSize / hand.GetSize();
            DrawSetTransform(pointer, 0.006f * Mathf.Sin(idleTime * 1.85f), Vector2.One);
            DrawTextureRect(hand, new Rect2(-hotspot, handSize), false, new Color(1, 1, 1, handOpacity));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }

    private void DrawCenterBanner()
    {
        Texture2D banner = result switch
        {
            "WIN" => WinArt,
            "LOSE" => LoseArt,
            _ => null
        };
        if (banner == null && countdown > 0 && countdown <= CountdownSeconds)
        {
            banner = Mathf.CeilToInt(countdown) switch
            {
                3 => Count3Art,
                2 => Count2Art,
                _ => Count1Art
            };
        }
        if (banner == null && goTime > 0)
            banner = GoArt;
        Layer(banner);
    }

    private void DrawGlassRim(float width, Color color)
    {
        for (int i = 0; i < 8; i++)
        {
            Vector2 offset = (Vector2.FromAngle(i * Mathf.Tau / 8) * width).Round();
            DrawTextureRect(glassSilhouette, new Rect2(offset, new Vector2(960, 600)), false, color);
        }
    }

    private void DrawInspectUsesCount()
    {
        DrawString(
            uiFont,
            new Vector2(InspectBadgeCenter.X - InspectBadgeWidth * 0.5f, InspectBadgeCenter.Y + InspectBadgeFontSize * 0.36f),
            playerInspectUses.ToString(),
            HorizontalAlignment.Center,
            InspectBadgeWidth,
            InspectBadgeFontSize,
            Colors.White
        );
    }

    private void DrawMagnifier(float opacity)
    {
        Vector2 lens = new Vector2(480, 300) + rivalSway * 1.5f;
        Rect2 viewport = GetViewportRect();
        Transform2D toLocal = GetGlobalTransformWithCanvas().AffineInverse();
        Vector2 corner = toLocal * viewport.Position;
        Color tint = new(1, 1, 1, opacity);
        DrawRect(new Rect2(corner, toLocal * viewport.End - corner).Grow(1), new Color(0.09f, 0.082f, 0.14f, opacity));
        Rect2 screen = new(0, 0, 960, 600);
        if (MagnifiedBackground != null) DrawTextureRect(MagnifiedBackground, screen, false, tint);
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
            float fade = opacity * (discarding.Contains(card) ? card.Alpha : 1f);
            overLensCards |= bounds.HasPoint((pointer - lens - new Vector2(x, y)).Rotated(-angle) / 1.85f);
            DrawSetTransform(lens + new Vector2(x, y), angle, Vector2.One * 1.85f);
            if (card.Rank == 0)
            {
                DrawRect(bounds.Grow(5), new Color(1f, 0.8f, 0.439f, 0.4f * fade));
                DrawRect(bounds.Grow(2), new Color(Gold.R, Gold.G, Gold.B, fade));
            }
            DrawRect(bounds, new Color(0.851f, 0.835f, 0.886f, 0.741f * fade));
            Texture2D art = Face(card);
            if (art != null)
                DrawTextureRect(art, bounds, false, new Color(1, 1, 1, 0.74f * fade));
            else
                DrawString(uiFont, new(-38, 9), card.Rank == 0 ? "JOKER" : card.Rank.ToString(),
                    HorizontalAlignment.Center, 76, card.Rank == 0 ? 20 : 30, new Color(Ink.R, Ink.G, Ink.B, fade));
            DrawRect(bounds, card.Rank == 0
                ? new Color(Gold.R, Gold.G, Gold.B, fade)
                : new Color(0.949f, 0.914f, 0.843f, fade), false, 1.5f);
        }
        Card moving = reachTime >= 0 && move != Move.Steal ? targetCard : null;
        foreach (Card card in rival)
            if (card != moving) DrawLiveCard(card);
        if (moving != null && rival.Contains(moving)) DrawLiveCard(moving);
        foreach (Card card in discarding)
            if (card.LeavingRivalHand) DrawLiveCard(card);
        if (carried != null && carried.Position.Y < 355) DrawLiveCard(carried);
        DrawSetTransform(lens + new Vector2(0, 104), 0, Vector2.One);
        if (SupportArt != null) DrawTextureRect(SupportArt, new Rect2(-87, -69, 174, 138), false, tint);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        if (MagnifiedFrame != null) DrawTextureRect(MagnifiedFrame, screen, false, tint);
    }
    private void DrawFan(List<Card> cards, bool upper, Vector2 sway, Card lifted = null)
    {
        for (int i = 0; i < cards.Count; i++)
        {
            Card card = cards[i];
            if (card == held || card == lifted) continue;
            float progress = 1;
            if (countdown > CountdownSeconds)
            {
                // The rival gets the first card and the final, odd card.
                int order = 2 * i + (upper ? 0 : 1);
                float time = dealDuration + CountdownSeconds - countdown - order * DealInterval;
                if (time <= 0) continue;
                progress = Phase(time, 0, DealFlight);
            }
            Paint(card, upper, sway, progress);
        }
    }

    private void Paint(Card card, bool hidden, Vector2 sway = default, float dealProgress = 1)
    {
        bool fading = discarding.Contains(card);
        float scale = fading ? card.DrawnScale : card == carried ? CarriedScale : hidden ? RivalScale : PlayerScale;
        float alpha = fading ? card.Alpha : 1f;
        Vector2 position = card.Position + sway;
        float angle = card.Angle;
        if (dealProgress < 1)
        {
            Vector2 origin = new(hidden ? 995 : -35, 367);
            position = origin.Lerp(position, dealProgress);
            position.Y -= 44 * Mathf.Sin(Mathf.Pi * dealProgress);
            angle = Mathf.LerpAngle(hidden ? -0.48f : 0.48f, angle, dealProgress);
            scale = Mathf.Lerp(0.7f, scale, dealProgress);
        }
        DrawSetTransform(position, angle, Vector2.One * scale);

        Texture2D tex = hidden ? Back : Face(card);
        if (tex != null)
        {
            DrawTextureRect(tex, bounds, false, new Color(1, 1, 1, alpha));
            if (!hidden) DrawRect(bounds, new Color(0.761f, 0.608f, 0.459f, alpha), false, 0.8f);
        }
        else if (hidden)
        {
            DrawRect(bounds.Grow(-5), new Color(Gold.R, Gold.G, Gold.B, alpha), false, 2);
            DrawColoredPolygon(new Vector2[]{new(0,-19),new(12,0),new(0,19),new(-12,0)}, new Color(Gold.R, Gold.G, Gold.B, alpha));
        }
        else
        {
            string label = card.Rank == 0 ? "J" : card.Rank.ToString();
            Color ink = new(Ink.R, Ink.G, Ink.B, alpha);
            DrawString(uiFont,new(-32,-39),label,fontSize:16,modulate:ink);
            DrawString(uiFont,new(-38,10),card.Rank == 0 ? "JOKER" : label,HorizontalAlignment.Center,76,card.Rank == 0 ? 20 : 32,ink);
        }
        if (hidden && card == landedCard && landingGlow > 0)
        {
            DrawRect(bounds, new Color(1, 0.85f, 0.46f, 0.34f * landingGlow));
            DrawRect(bounds, new Color(1, 0.92f, 0.65f, landingGlow), false, 2);
        }
        bool selected = card == held || (held == null && (card == hoveredMine || card == hoveredRival));
        bool matchingRank = held != null && !fromRival && held.Rank > 0 && card != held
            && card.Rank == held.Rank && mine.Contains(card);
        if (selected || matchingRank)
        {
            DrawRect(bounds.Grow(2), new Color("#ffb83e66"), false, 3);
            DrawRect(bounds, new Color("#fff1a032"));
            DrawRect(bounds.Grow(-1), new Color("#ffdf86"), false, 2);
            if (matchingRank && card == pendingPair)
                DrawRect(bounds.Grow(-2), new Color("#fff5bb"), false, 3);
        }
        DrawSetTransform(Vector2.Zero,0,Vector2.One);
    }
}
