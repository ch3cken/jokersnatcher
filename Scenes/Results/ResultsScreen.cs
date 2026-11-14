using Godot;

public partial class ResultsScreen : Control
{
	private TextureRect _centerImage;
	private Label _captionLabel;
	private Label _continueLabel;

	public override void _Ready()
	{
		_centerImage = GetNode<TextureRect>("MainLayout/VBoxContainer/CenterContainer/CenterGroup/CenterImage");
		_captionLabel = GetNode<Label>("MainLayout/VBoxContainer/CenterContainer/CenterGroup/CaptionLabel");
		_continueLabel = GetNode<Label>("MainLayout/VBoxContainer/ContinueLabel");
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.Left)
		{
			GetTree().ChangeSceneToFile("res://Scenes/Title/title_screen.tscn");
		}
	}
}
