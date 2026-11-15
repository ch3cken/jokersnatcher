using Godot;

public partial class BgmManager : Node
{
	public static BgmManager Instance { get; private set; }

	[Export] public AudioStream TitleTrack { get; set; }
	[Export] public AudioStream GameTrack { get; set; }
	[Export] public float FadeOutSeconds { get; set; } = 1f;

	private AudioStreamPlayer _player;
	private Tween _fade;

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _Ready()
	{
		_player = new AudioStreamPlayer { Bus = "Master" };
		AddChild(_player);
	}

	public void TitleBGM() => Play(TitleTrack);

	public void GameBGM() => Play(GameTrack);

	public void StopBGM()
	{
		if (_player == null || !_player.Playing) return;

		_fade?.Kill();
		float from = Mathf.DbToLinear(_player.VolumeDb);
		_fade = CreateTween();
		_fade.SetPauseMode(Tween.TweenPauseMode.Process);
		_fade.TweenMethod(Callable.From<float>(SetLinearVolume), from, 0f, FadeOutSeconds)
			.SetTrans(Tween.TransitionType.Sine)
			.SetEase(Tween.EaseType.InOut);
		_fade.TweenCallback(Callable.From(FinishStop));
	}

	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest)
			FinishStop();
	}

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
		FinishStop();
	}

	private void Play(AudioStream stream)
	{
		if (_player == null || stream == null) return;

		_fade?.Kill();
		_player.VolumeDb = 0;
		if (_player.Playing && _player.Stream == stream) return;

		if (stream is AudioStreamOggVorbis ogg)
			ogg.Loop = true;
		_player.Stream = stream;
		_player.Play();
	}

	private void SetLinearVolume(float linear)
	{
		if (_player == null) return;
		_player.VolumeDb = linear <= 0.0001f ? -80f : Mathf.LinearToDb(linear);
	}

	private void FinishStop()
	{
		_fade?.Kill();
		_fade = null;
		if (_player == null) return;
		_player.Stop();
		_player.Stream = null;
		_player.VolumeDb = 0;
	}
}
