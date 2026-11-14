using Godot;

public partial class SoundEffectManager : Node
{
	public static SoundEffectManager Instance { get; private set; }

	[Export] public AudioStream[] SoundEffects { get; set; } = [];

	private const int PlayerPoolSize = 8;
	private readonly AudioStreamPlayer[] _players = new AudioStreamPlayer[PlayerPoolSize];
	private int _nextPlayerIndex = 0;
	private readonly RandomNumberGenerator _rng = new();

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _Ready()
	{
		for (int i = 0; i < PlayerPoolSize; i++)
		{
			_players[i] = new AudioStreamPlayer();
			AddChild(_players[i]);
		}
	}

	public void PlaySound(int index)
	{
		if (SoundEffects == null || index < 0 || index >= SoundEffects.Length)
		{
			return;
		}

		AudioStream stream = SoundEffects[index];
		if (stream == null)
		{
			return;
		}

		AudioStreamPlayer player = _players[_nextPlayerIndex];
		_nextPlayerIndex = (_nextPlayerIndex + 1) % PlayerPoolSize;
		player.Stream = stream;
		player.Play();
	}

	public void PlayTakeCard() => PlaySound(0);
}
