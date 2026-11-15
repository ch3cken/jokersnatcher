using Godot;

public partial class TitleScreen : Control
{
	[Export(PropertyHint.Range, "10,1000,1")]
	public float CatRiseSpeed { get; set; } = 150.0f;

	private TextureRect _catRect;
	private TextureRect _cardsRect;
	private float _swayTime = 0.0f;
	private Button _settingsButton;
	private PopupPanel _settingsPopup;
	private Control _tutorialPopup;
	private CheckBox _fullscreenCheckBox;
	private HSlider _volumeSlider;

	private ulong _lastPopupHideMsec = 0;

	public override void _Ready()
	{
		_catRect = GetNode<TextureRect>("Background/CatRect");
		_catRect.Position = new Vector2(_catRect.Position.X, 200.0f);

		_cardsRect = GetNode<TextureRect>("Background/CatRect/Cards");
		_cardsRect.Position = Vector2.Zero;
		_swayTime = 0.0f;

		_settingsButton = GetNode<Button>("DifficultyButtons/SettingsButton");
		foreach (Node child in GetNode("DifficultyButtons").GetChildren())
		{
			if (child is Button button)
				button.MouseEntered += OnButtonHovered;
		}
		_settingsPopup = GetNode<PopupPanel>("SettingsPopup");
		_tutorialPopup = GetNode<Control>("TutorialPopup");
		_fullscreenCheckBox = GetNode<CheckBox>("SettingsPopup/MarginContainer/VBoxContainer/FullscreenCheckBox");
		_volumeSlider = GetNode<HSlider>("SettingsPopup/MarginContainer/VBoxContainer/VolumeSlider");
		Button tutorialButton = GetNode<Button>("SettingsPopup/MarginContainer/VBoxContainer/TutorialButton");
		tutorialButton.MouseEntered += OnButtonHovered;

		// Track when PopupPanel hides so clicking SettingsButton to close it doesn't immediately reopen it
		_settingsPopup.PopupHide += OnSettingsPopupHide;

		// Ensure the pop-ups start hidden
		_settingsPopup.Hide();
		_tutorialPopup.Hide();

		// Sync the checkbox with the current window mode without firing the Toggled signal
		DisplayServer.WindowMode currentMode = DisplayServer.WindowGetMode();
		bool isFullscreen = currentMode == DisplayServer.WindowMode.Fullscreen
			|| currentMode == DisplayServer.WindowMode.ExclusiveFullscreen;
		_fullscreenCheckBox.SetPressedNoSignal(isFullscreen);

		// Sync the volume slider with the global Master audio bus volume
		int masterBusIndex = AudioServer.GetBusIndex("Master");
		float currentLinearVolume = Mathf.DbToLinear(AudioServer.GetBusVolumeDb(masterBusIndex));
		_volumeSlider.SetValueNoSignal(currentLinearVolume);
		BgmManager.Instance?.TitleBGM();
		BuildOnlinePanel();
	}

	private Label _onlineStatus;
	private LineEdit _addressField;

	private void BuildOnlinePanel()
	{
		var panel = new PanelContainer
		{
			Position = new Vector2(640, 210),
			CustomMinimumSize = new Vector2(440, 0)
		};
		panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.14f, 0.09f, 0.07f, 0.88f),
			BorderColor = new Color(0.93f, 0.78f, 0.55f, 0.9f),
			BorderWidthBottom = 3,
			BorderWidthLeft = 3,
			BorderWidthRight = 3,
			BorderWidthTop = 3,
			CornerRadiusBottomLeft = 14,
			CornerRadiusBottomRight = 14,
			CornerRadiusTopLeft = 14,
			CornerRadiusTopRight = 14,
			ContentMarginLeft = 18,
			ContentMarginTop = 16,
			ContentMarginRight = 18,
			ContentMarginBottom = 16
		});

		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 10);
		panel.AddChild(box);

		box.AddChild(new Label
		{
			Text = "온라인 대전",
			HorizontalAlignment = HorizontalAlignment.Center
		});

		var hostButton = new Button { Text = "방 만들기", CustomMinimumSize = new Vector2(0, 44) };
		hostButton.Pressed += () =>
		{
			SoundEffectManager.Instance?.PlayButtonClick();
			NetPlay.Instance?.Host();
		};
		box.AddChild(hostButton);

		_addressField = new LineEdit
		{
			PlaceholderText = "상대 IP",
			CustomMinimumSize = new Vector2(0, 44)
		};
		box.AddChild(_addressField);

		var joinButton = new Button { Text = "참가", CustomMinimumSize = new Vector2(0, 44) };
		joinButton.Pressed += () =>
		{
			SoundEffectManager.Instance?.PlayButtonClick();
			NetPlay.Instance?.Join(_addressField.Text);
		};
		box.AddChild(joinButton);

		_onlineStatus = new Label
		{
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(390, 96)
		};
		box.AddChild(_onlineStatus);
		AddChild(panel);
	}

	public override void _Process(double delta)
	{
		if (_catRect.Position.Y > 0.0f)
		{
			float newY = Mathf.MoveToward(_catRect.Position.Y, 0.0f, CatRiseSpeed * (float)delta);
			_catRect.Position = new Vector2(_catRect.Position.X, newY);
		}
		else
		{
			_swayTime += (float)delta;
			_cardsRect.Position = new Vector2(
				4f * Mathf.Sin(_swayTime * 1.32f),
				8f * Mathf.Sin(_swayTime * 1.63f)
			);
		}
		if (_onlineStatus != null && NetPlay.Instance != null)
			_onlineStatus.Text = NetPlay.Instance.Status;
	}

	private void OnSettingsPopupHide()
	{
		_lastPopupHideMsec = Time.GetTicksMsec();
	}

	private void OnButtonHovered()
	{
		SoundEffectManager.Instance?.PlayButtonHover();
	}

	public void OnPlayButtonPressed(int difficulty)
	{
		if (_tutorialPopup.Visible)
		{
			return;
		}

		SoundEffectManager.Instance?.PlayButtonClick();
		SoundEffectManager.Instance?.PlayGameStart();
		NetPlay.Instance?.Leave();
		PackedScene gamePackedScene = GD.Load<PackedScene>("res://Scenes/Game/GameScene.tscn");
		GameScene gameScene = gamePackedScene.Instantiate<GameScene>();
		gameScene.Difficulty = (OpponentDifficulty)difficulty;

		SceneTree tree = GetTree();
		Node currentScene = tree.CurrentScene;
		tree.Root.AddChild(gameScene);
		tree.CurrentScene = gameScene;
		currentScene?.QueueFree();
	}

	public void OnSettingsButtonPressed()
	{
		if (_tutorialPopup.Visible)
		{
			return;
		}

		SoundEffectManager.Instance?.PlayButtonClick();
		if (_settingsPopup.Visible)
		{
			_settingsPopup.Hide();
			return;
		}

		// Because PopupPanel automatically hides on mouse-down when clicking outside of it,
		// clicking the SettingsButton while the popup is open will hide the popup right before
		// this Pressed callback runs. This guard prevents it from immediately popping back up.
		if (Time.GetTicksMsec() - _lastPopupHideMsec < 250)
		{
			return;
		}

		// Position the PopupPanel right above the SettingsButton so the triangle tip points at the button's center
		Rect2 buttonRect = _settingsButton.GetGlobalRect();
		Vector2I popupSize = _settingsPopup.Size;
		int x = Mathf.RoundToInt(buttonRect.End.X - popupSize.X);
		int y = Mathf.RoundToInt(buttonRect.Position.Y - popupSize.Y - 6);

		_settingsPopup.Popup(new Rect2I(x, y, popupSize.X, popupSize.Y));
	}

	public void OnTutorialButtonPressed()
	{
		SoundEffectManager.Instance?.PlayButtonClick();
		_settingsPopup.Hide();
		_tutorialPopup.Show();
	}

	public void OnTutorialPopupGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.Left)
		{
			SoundEffectManager.Instance?.PlayButtonClick();
			_tutorialPopup.Hide();
			_settingsPopup.Hide();
			GetViewport().SetInputAsHandled();
		}
	}

	public void OnFullscreenCheckBoxToggled(bool toggledOn)
	{
		SoundEffectManager.Instance?.PlayButtonClick();
		if (toggledOn)
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
		}
		else
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
		}
	}

	public void OnVolumeSliderValueChanged(double value)
	{
		int masterBusIndex = AudioServer.GetBusIndex("Master");
		AudioServer.SetBusVolumeDb(masterBusIndex, Mathf.LinearToDb((float)value));
		AudioServer.SetBusMute(masterBusIndex, value <= 0.001);
	}
}
