using Godot;

public partial class SoundEffectManager : Node
{
	public static SoundEffectManager Instance { get; private set; }

	[Export] public AudioStream[] SoundEffects { get; set; } = [];

	private const int PlayerPoolSize = 8;
	private readonly AudioStreamPlayer[] _players = new AudioStreamPlayer[PlayerPoolSize];
	private int _nextPlayerIndex = 0;
	private readonly RandomNumberGenerator _rng = new();
	private bool _closing;

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _Ready()
	{
		GetTree().AutoAcceptQuit = false;
		_rng.Randomize();
		for (int i = 0; i < PlayerPoolSize; i++)
		{
			_players[i] = new AudioStreamPlayer();
			AddChild(_players[i]);
		}
	}

	public void PlaySound(int index)
	{
		if (_closing || SoundEffects == null || index < 0 || index >= SoundEffects.Length)
		{
			return;
		}

		AudioStream stream = SoundEffects[index];
		if (stream == null)
		{
			return;
		}

		// A scene can request a sound and close in the same frame (e.g. the opening deal).
		Callable.From(() => StartSound(stream)).CallDeferred();
	}

	private void StartSound(AudioStream stream)
	{
		if (_closing || !IsInsideTree()) return;
		AudioStreamPlayer player = _players[_nextPlayerIndex];
		_nextPlayerIndex = (_nextPlayerIndex + 1) % PlayerPoolSize;
		player.Stream = stream;
		player.PitchScale = _rng.RandfRange(0.9f, 1.1f);
		player.Play();
	}

	public override async void _Notification(int what)
	{
		if (what != NotificationWMCloseRequest || _closing || !IsInsideTree()) return;
		_closing = true;
		var tree = GetTree();
		StopSounds();
		// The mixer releases stopped playbacks asynchronously, across engine frames.
		await ToSignal(tree.CreateTimer(0.2, ignoreTimeScale: true), SceneTreeTimer.SignalName.Timeout);
		if (IsInsideTree()) tree.Quit();
	}

	private void StopSounds()
	{
		foreach (var player in _players)
		{
			if (player == null) continue;
			player.Stop();
			player.Stream = null;
		}
	}

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
		StopSounds();
	}

	public void PlayTakeCard() => PlaySound((int)Effect.TakeCard);
	public void PlayCard() => PlaySound(_rng.RandiRange((int)Effect.Card1, (int)Effect.Card4));
	public void PlayJoker() => PlaySound((int)Effect.ItsTheJoker);
	public void PlayDeal() => PlaySound((int)Effect.Deal);
	public void PlayButtonHover() => PlaySound((int)Effect.ButtonHover);
	public void PlayButtonClick() => PlaySound((int)Effect.ButtonClick);
	public void PlayGameStart() => PlaySound((int)Effect.GameStart);
	public void PlayCountdown() => PlaySound((int)Effect.Countdown);
	public void PlayDefeat() => PlaySound((int)Effect.Defeat);
	public void PlayVictory() => PlaySound((int)Effect.Victory);
	public void PlayResultEnter() => PlaySound((int)Effect.ResultEnter);
	public void PlayMagnifierStart() => PlaySound((int)Effect.MagnifierStart);
	public void PlayMagnifierEnd() => PlaySound((int)Effect.MagnifierEnd);
	public void PlayEnemyMagnifier() => PlaySound((int)Effect.EnemyMagnifier);
	public void PlayReturnToMain() => PlaySound((int)Effect.ReturnToMain);
	public void PlayGo()
	{
		PlaySound((int)Effect.EnemyMagnifier);
		PlaySound((int)Effect.Sell);
	}

	private enum Effect
	{
		TakeCard,
		TakeCardAlt,
		ButtonHover,
		ButtonClick,
		GameStart,
		Countdown,
		Defeat,
		Victory,
		ResultEnter,
		MagnifierStart,
		MagnifierEnd,
		EnemyMagnifier,
		ReturnToMain,
		Card1,
		Card2,
		Card3,
		Card4,
		Deal,
		Sell,
		ItsTheJoker,
	}
}
