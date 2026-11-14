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
        Layout(1 - Mathf.Exp(-18 * (float)delta));
        QueueRedraw();
    }

    private static Vector2 Slot(int i, int total, bool upper)
    {
        float t = i - (total - 1) / 2f;
        float arc = Mathf.Pow(t / Mathf.Max(1, (total - 1) / 2f), 2);
        return new(480 + t * Mathf.Min(64, 740f / Mathf.Max(1, total - 1)),
                   upper ? 194 + arc * 13 : 484 + arc * 23);
    }

    private void Layout(float weight)
    {
        foreach (bool upper in new[] { false, true })
        {
            var cards = upper ? rival : mine;
            int count = cards.Count(c => c != held);
            bool opening = !upper && held != null && !fromRival && match == null;
            int slots = count + (opening ? 1 : 0);
            int i = 0;
            foreach (Card card in cards)
            {
                if (card == held) continue;
                if (opening && i == gap) i++;
                card.Position = card.Position.Lerp(Slot(i, slots, upper), weight);
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
            fromRival = false;
            if (held != null) Aim();
        }
        else if (!button.Pressed && held != null)
        {
            Aim();
            if (match != null)
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
        DrawCircle(new(480,128), 77, Paper);
        DrawColoredPolygon(new Vector2[]{new(410,108),new(416,31),new(460,64)}, Paper);
        DrawColoredPolygon(new Vector2[]{new(500,64),new(548,32),new(550,108)}, Paper);
        DrawCircle(new(453,115),5,Ink);
        DrawCircle(new(505,115),5,Ink);
        DrawRect(new Rect2(0,233,960,367), new Color("#48446b"));
        DrawLine(new(0,235),new(960,235),new Color("#78719a"),4);
        if (TableArt != null) DrawTextureRect(TableArt,new Rect2(0,0,960,600),false);
        foreach (var card in rival) if (card != held) Paint(card, true);
        foreach (var card in mine) if (card != held) Paint(card, false);
        foreach (var card in discarding) Paint(card, false);
        if (held != null) Paint(held, fromRival);
        if (HandArt != null) DrawTextureRect(HandArt,new Rect2(pointer,new Vector2(48,48)),false);
        else
        {
            DrawCircle(pointer + new Vector2(12,18), 12, new Color("#cb834f"));
            DrawLine(pointer + new Vector2(8,14),pointer,new Color("#efaa72"),7,true);
            DrawCircle(pointer,3.5f,new Color("#efaa72"));
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
