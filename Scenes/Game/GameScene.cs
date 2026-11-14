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

     [Export] public Texture2D BodyArt { get; set; }
    [Export] public Texture2D GlassArt { get; set; }
    [Export] public Texture2D ArmArt { get; set; }
    [Export] public Texture2D RivalPawArt { get; set; }

    private sealed class Card(int rank)
    {
        public readonly int Rank = rank;
        public Vector2 Position;
        public float Angle;
    }

    private readonly List<Card> mine = new();
    private readonly List<Card> rival = new();
    private readonly Rect2 bounds = new(-38, -60, 76, 120);
    private readonly RandomNumberGenerator rng = new();
    private readonly List<Card> discarding = new();
    private Card held, match;
    private bool fromRival;
    private int gap;
    private Vector2 pointer;
    [Export(PropertyHint.Range, "1,20,0.1")] public float StealInterval { get; set; } = 5;
    private static readonly Vector2 ArmRest = new(625, 146), ArmRaised = new(645, -65);
    private Vector2 paw = ArmRest, reachEnd;
    private Card targetCard, carried;
    private float reachTime = -1, restTime;

    private void UpdateArm(float dt)
    {
        if (countdown > 0 || result != null) return;
        if (reachTime < 0)
        {
            restTime += dt;
            if (restTime < StealInterval || mine.Count == 0) return;
            targetCard = mine[rng.RandiRange(0, mine.Count - 1)];
            reachEnd = targetCard.Position;
            reachTime = restTime = 0;
        }
        reachTime += dt;
        if (reachTime < 0.42f)
            paw = ArmRest.Lerp(ArmRaised, Mathf.SmoothStep(0, 1, reachTime / 0.42f));
        else if (reachTime < 1.04f)
        {
            if (mine.Contains(targetCard)) reachEnd = targetCard.Position;
            paw = ArmRaised.Lerp(reachEnd, Mathf.SmoothStep(0, 1, (reachTime - 0.42f) / 0.62f));
        }
        else
        {
            if (targetCard != null)
            {
                if (mine.Remove(targetCard))
                {
                    carried = targetCard;
                    reachEnd = carried.Position;
                    if (held == carried) held = match = null;
                    else if (match == carried) match = null;
                }
                targetCard = null;
            }
            Vector2 home = Slot(rival.Count, rival.Count + 1, true);
            paw = reachTime < 1.8f
                ? reachEnd.Lerp(home, Mathf.SmoothStep(0, 1, (reachTime - 1.15f) / 0.65f))
                : home.Lerp(ArmRest, Mathf.SmoothStep(0, 1, (reachTime - 1.8f) / 0.32f));
            if (carried != null)
            {
                carried.Position = paw;
                if (reachTime >= 1.8f)
                {
                    rival.Add(carried);
                    carried = null;
                    CheckResult();
                }
            }
            if (reachTime >= 2.12f) reachTime = -1;
        }
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
        foreach (int rank in new[] { 3, 7, 1, 5, 3, 0, 8, 7, 3, 9 }) mine.Add(new Card(rank));
        foreach (int rank in new[] { 2, 6, 4, 10, 1, 5, 8, 9, 2, 6 }) rival.Add(new Card(rank));
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
        Layout(1 - Mathf.Exp(-18 * (float)delta));
        QueueRedraw();
    }

    private static Vector2 Slot(int i, int total, bool upper)
    {
        float t = i - (total - 1) / 2f;
        float arc = Mathf.Pow(t / Mathf.Max(1, (total - 1) / 2f), 2);
        return new((upper ? 484 : 300) + t * Mathf.Min(upper ? 25 : 29, 470f / Mathf.Max(1, total - 1)),
                   upper ? 232 + arc * 13 : 484 + arc * 23);
    }

    private void Layout(float weight)
    {
        foreach (bool upper in new[] { false, true })
        {
            var cards = upper ? rival : mine;
            int count = cards.Count(c => c != held);
            bool opening = !upper && held != null && !fromRival && match == null;
            int slots = count + (opening ? 1 : 0);
            int focus = -1;
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
                    if (distance == 0) destination.Y -= 8;
                }
                card.Position = card.Position.Lerp(destination, weight);
                card.Angle = Mathf.Lerp(card.Angle, (i - (slots - 1) / 2f) * 0.022f, weight);
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
        if (countdown > 0 || result != null || input is not InputEventMouseButton button ||
            button.ButtonIndex != MouseButton.Left) return;
        pointer = GetGlobalTransformWithCanvas().AffineInverse() * button.Position;
        if (button.Pressed && held == null)
        {
            held = Pick(mine);
            fromRival = held == null;
            if (fromRival) held = Pick(rival);
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
                    mine.Insert(rng.RandiRange(0, mine.Count), held);
                }
            }
            else if (match != null)
            {
                Discard(match);
                Discard(held);
            }
            else if (pointer.Y >= 355)
            {
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
        if (what == NotificationApplicationFocusOut) held = match = null;
    }

    private void Aim()
    {
        int others = mine.Count(c => c != held);
        gap = 0;
        while (gap < others && pointer.X > Slot(gap, others, false).X) gap++;
        Card target = Pick(mine);
        match = !fromRival && held.Rank > 0 && target?.Rank == held.Rank ? target : null;
        held.Position = match == null ? pointer : match.Position + new Vector2(5,-5);
        held.Angle = 0;
    }
    private void Discard(Card card)
    {
        mine.Remove(card);
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
        if (GlassArt != null) DrawTextureRect(GlassArt,new Rect2(684,439,139,113),false);
        foreach (var card in rival) if (card != held) Paint(card, true);
        foreach (var card in mine) if (card != held) Paint(card, false);
        foreach (var card in discarding) Paint(card, false);
        if (held != null) Paint(held, fromRival);
        if (carried != null) Paint(carried, true);
        if (ArmArt != null && paw.Y > -30)
            DrawTextureRect(ArmArt,new Rect2(paw.X - 23,-30,46,paw.Y + 30),false);
        if (RivalPawArt != null)
            DrawTextureRect(RivalPawArt,new Rect2(paw - new Vector2(39,19),new Vector2(78,65)),false);
        if (HandArt != null)
        {
            Vector2 tip = new Rect2(0,0,960,600).HasPoint(pointer) ? pointer : new Vector2(738,341);
            DrawTextureRect(HandArt,new Rect2(tip - new Vector2(4,18),new Vector2(90,75)),false);
            // The other paw supports the player hand from underneath.
            DrawTextureRect(HandArt,new Rect2(222,534,124,103),false);
        }
        if (countdown > 0 || result != null)
            DrawString(ThemeDB.FallbackFont,new(0,325),result ?? Mathf.CeilToInt(countdown).ToString(),HorizontalAlignment.Center,960,52,Gold);
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
        if (held != null && card != held && !hidden && !fromRival && held.Rank > 0 && card.Rank == held.Rank)
            DrawRect(bounds.Grow(1),Gold,false,4);
        DrawSetTransform(Vector2.Zero,0,Vector2.One);
    }
}
