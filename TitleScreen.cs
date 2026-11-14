using Godot;

public partial class TitleScreen : Control
{
	private Button _settingsButton;
	private PopupPanel _settingsPopup;
	private CheckBox _fullscreenCheckBox;
	private HSlider _volumeSlider;

	private ulong _lastPopupHideMsec = 0;

	public override void _Ready()
	{
		_settingsButton = GetNode<Button>("MainLayout/VBoxContainer/DifficultyButtons/SettingsButton");
		_settingsPopup = GetNode<PopupPanel>("SettingsPopup");
		_fullscreenCheckBox = GetNode<CheckBox>("SettingsPopup/MarginContainer/VBoxContainer/FullscreenCheckBox");
		_volumeSlider = GetNode<HSlider>("SettingsPopup/MarginContainer/VBoxContainer/VolumeSlider");

		// Track when PopupPanel hides so clicking SettingsButton to close it doesn't immediately reopen it
		_settingsPopup.PopupHide += OnSettingsPopupHide;

		// Ensure the pop-up starts hidden
		_settingsPopup.Hide();

		// Sync the checkbox with the current window mode without firing the Toggled signal
		DisplayServer.WindowMode currentMode = DisplayServer.WindowGetMode();
		bool isFullscreen = currentMode == DisplayServer.WindowMode.Fullscreen
			|| currentMode == DisplayServer.WindowMode.ExclusiveFullscreen;
		_fullscreenCheckBox.SetPressedNoSignal(isFullscreen);

		// Sync the volume slider with the global Master audio bus volume
		int masterBusIndex = AudioServer.GetBusIndex("Master");
		float currentLinearVolume = Mathf.DbToLinear(AudioServer.GetBusVolumeDb(masterBusIndex));
		_volumeSlider.SetValueNoSignal(currentLinearVolume);
	}

	private void OnSettingsPopupHide()
	{
		_lastPopupHideMsec = Time.GetTicksMsec();
	}

	public void OnSettingsButtonPressed()
	{
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

	public void OnFullscreenCheckBoxToggled(bool toggledOn)
	{
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
