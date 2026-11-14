using Godot;

public partial class ResultsScreen : Control
{
	[Export] public bool PlayerWon { get; set; } = true;
	[Export] public string WinMessage { get; set; } = "You are the Joker Snatcher!!";
	[Export] public string LoseMessage { get; set; } = "You ended as the clown...";
	[Export] public Texture2D WinImage { get; set; }
	[Export] public Texture2D LoseImage { get; set; }
	[Export] public double FadeInDurationSeconds { get; set; } = 0.4;

	private Control _mainLayout;
	private TextureRect _centerImage;
	private Label _captionLabel;
	private Label _continueLabel;
	private bool _canContinue = false;

	public override void _Ready()
	{
		SoundEffectManager.Instance?.PlayResultEnter();
		_mainLayout = GetNode<Control>("MainLayout");
		_centerImage = GetNode<TextureRect>("MainLayout/VBoxContainer/CenterContainer/CenterGroup/CenterImage");
		_captionLabel = GetNode<Label>("MainLayout/VBoxContainer/CenterContainer/CenterGroup/CaptionLabel");
		_continueLabel = GetNode<Label>("MainLayout/VBoxContainer/ContinueLabel");

		// Set the central message (and optional image) based on whether the player won or lost
		_captionLabel.Text = PlayerWon ? WinMessage : LoseMessage;

		if (PlayerWon && WinImage != null)
		{
			_centerImage.Texture = WinImage;
		}
		else if (!PlayerWon && LoseImage != null)
		{
			_centerImage.Texture = LoseImage;
		}

		// Smoothly fade in the UI over the black background
		_mainLayout.Modulate = new Color(1, 1, 1, 0);
		Tween tween = CreateTween();
		tween.TweenProperty(_mainLayout, "modulate:a", 1.0f, FadeInDurationSeconds)
			.SetTrans(Tween.TransitionType.Sine)
			.SetEase(Tween.EaseType.Out);
		tween.TweenCallback(Callable.From(() => _canContinue = true));
	}

	public override void _Input(InputEvent @event)
	{
		if (!_canContinue)
		{
			return;
		}

		if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.Left)
		{
			_canContinue = false;
			SoundEffectManager.Instance?.PlayReturnToMain();
			GetTree().ChangeSceneToFile("res://Scenes/Title/title_screen.tscn");
		}
	}
}
