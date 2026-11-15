using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class NetPlay : Node
{
	public static NetPlay Instance { get; private set; }

	public const int Port = 24565;

	public bool IsHost { get; private set; }
	public bool InMatch { get; private set; }
	public string Deal { get; private set; } = "";
	public string Status { get; private set; } = "";

	private enum Phase { Idle, Hosting, Connecting, Playing }

	private readonly Queue<Decision> _actions = new();
	private readonly RandomNumberGenerator _rng = new();
	private Phase _phase = Phase.Idle;
	private bool _matchStarted;
	private bool _gameOpen;
	private bool _expectDisconnect;

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _Ready()
	{
		_rng.Randomize();
		Multiplayer.PeerConnected += OnPeerConnected;
		Multiplayer.PeerDisconnected += _ => HandleDrop("상대 연결이 끊어졌습니다");
		Multiplayer.ConnectedToServer += () => Status = "접속했습니다. 대전을 준비하는 중...";
		Multiplayer.ConnectionFailed += OnConnectionFailed;
		Multiplayer.ServerDisconnected += () => HandleDrop("방장과 연결이 끊어졌습니다");
	}

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
	}

	public void Host()
	{
		_phase = Phase.Idle;
		Shutdown();
		var peer = new ENetMultiplayerPeer();
		Error error = peer.CreateServer(Port, 1);
		if (error != Error.Ok)
		{
			Status = "방을 열지 못했습니다. 포트 " + Port + "를 확인해 주세요.";
			peer.Close();
			return;
		}

		Multiplayer.MultiplayerPeer = peer;
		IsHost = true;
		_phase = Phase.Hosting;
		_matchStarted = false;
		_gameOpen = false;
		_expectDisconnect = false;
		Status = "방을 열었습니다. 상대에게 이 주소를 알려 주세요.\n" + LocalAddresses() + "\n포트 " + Port;
	}

	public void Join(string address)
	{
		address = address?.Trim() ?? "";
		if (address.Length == 0)
		{
			Status = "상대 IP를 입력해 주세요.";
			return;
		}

		string host = address;
		int port = Port;
		int colon = address.LastIndexOf(':');
		if (colon > 0 && int.TryParse(address[(colon + 1)..], out int parsed))
		{
			host = address[..colon];
			port = parsed;
		}

		_phase = Phase.Idle;
		Shutdown();
		var peer = new ENetMultiplayerPeer();
		Error error = peer.CreateClient(host, port);
		if (error != Error.Ok)
		{
			Status = "접속을 시작하지 못했습니다.";
			peer.Close();
			return;
		}

		Multiplayer.MultiplayerPeer = peer;
		IsHost = false;
		_phase = Phase.Connecting;
		_gameOpen = false;
		_expectDisconnect = false;
		Status = host + ":" + port + " 에 접속하는 중...";
	}

	public void Leave()
	{
		_expectDisconnect = true;
		_phase = Phase.Idle;
		_matchStarted = false;
		_gameOpen = false;
		InMatch = false;
		Deal = "";
		_actions.Clear();
		Shutdown();
		Status = "";
		_expectDisconnect = false;
	}

	public void EndMatch()
	{
		_expectDisconnect = true;
		_phase = Phase.Idle;
		_matchStarted = false;
		_gameOpen = false;
		InMatch = false;
		_actions.Clear();
		Shutdown();
	}

	public void Send(string action)
	{
		if (!InMatch || Multiplayer.MultiplayerPeer == null) return;
		Rpc(nameof(ReceiveAction), action);
	}

	internal bool TryTakeAction(out Decision decision)
	{
		if (_actions.Count == 0)
		{
			decision = default;
			return false;
		}

		decision = _actions.Dequeue();
		return true;
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ReceiveDeal(string deal)
	{
		Deal = deal;
		IsHost = false;
		Callable.From(OpenGame).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ReceiveAction(string action)
	{
		if (TryParse(action, out Decision decision))
			_actions.Enqueue(decision);
	}

	private void OnPeerConnected(long id)
	{
		if (!Multiplayer.IsServer() || _matchStarted) return;
		_matchStarted = true;
		Deal = BuildDeal();
		RpcId(id, nameof(ReceiveDeal), Deal);
		Callable.From(OpenGame).CallDeferred();
	}

	private void OnConnectionFailed()
	{
		_phase = Phase.Idle;
		Shutdown();
		Status = "접속에 실패했습니다. IP와 포트를 확인해 주세요.";
	}

	private void HandleDrop(string message)
	{
		if (_phase == Phase.Idle) return;
		Phase phase = _phase;
		bool decided = GetTree()?.CurrentScene is GameScene game && game.MatchDecided;
		_phase = Phase.Idle;
		_matchStarted = false;
		_gameOpen = false;
		InMatch = false;
		_actions.Clear();
		Shutdown();
		if (_expectDisconnect || phase != Phase.Playing || decided) return;
		Status = message;
		Callable.From(ReturnToTitle).CallDeferred();
	}

	private void OpenGame()
	{
		if (_gameOpen) return;
		_gameOpen = true;
		_phase = Phase.Playing;
		InMatch = true;
		_actions.Clear();

		PackedScene packed = GD.Load<PackedScene>("res://Scenes/Game/GameScene.tscn");
		GameScene game = packed.Instantiate<GameScene>();
		SceneTree tree = GetTree();
		Node current = tree.CurrentScene;
		tree.Root.AddChild(game);
		tree.CurrentScene = game;
		current?.QueueFree();
	}

	private void ReturnToTitle()
	{
		SceneTree tree = GetTree();
		if (tree == null || tree.CurrentScene is TitleScreen) return;
		tree.ChangeSceneToFile("res://Scenes/Title/title_screen.tscn");
	}

	private void Shutdown()
	{
		if (Multiplayer.MultiplayerPeer == null) return;
		MultiplayerPeer peer = Multiplayer.MultiplayerPeer;
		Multiplayer.MultiplayerPeer = null;
		peer.Close();
	}

	private string BuildDeal()
	{
		var deck = new List<(int Rank, bool Second)>(20);
		for (int rank = 1; rank <= 10; rank++)
		{
			deck.Add((rank, false));
			deck.Add((rank, true));
		}
		for (int i = deck.Count - 1; i > 0; i--)
		{
			int j = _rng.RandiRange(0, i);
			(deck[i], deck[j]) = (deck[j], deck[i]);
		}

		var host = deck.GetRange(0, 10);
		var guest = deck.GetRange(10, 10);
		guest.Insert(_rng.RandiRange(0, guest.Count), (0, false));
		return Encode(host) + "|" + Encode(guest);
	}

	private static string Encode(IEnumerable<(int Rank, bool Second)> cards) =>
		string.Join(",", cards.Select(card => card.Rank + (card.Second ? "b" : "a")));

	private static bool TryParse(string action, out Decision decision)
	{
		decision = default;
		string[] parts = action.Split('|');
		if (parts.Length == 0) return false;
		switch (parts[0])
		{
			case "I":
				decision = new Decision(Move.Inspect);
				return true;
			case "R" when parts.Length == 3 && int.TryParse(parts[1], out int from) && int.TryParse(parts[2], out int to):
				decision = new Decision(Move.Shuffle, from, to);
				return true;
			case "P" when parts.Length == 3 && int.TryParse(parts[1], out int a) && int.TryParse(parts[2], out int b):
				decision = new Decision(Move.Pair, a, b);
				return true;
			case "S" when parts.Length == 3 && int.TryParse(parts[1], out int stolen) && int.TryParse(parts[2], out int slot):
				decision = new Decision(Move.Steal, stolen, slot);
				return true;
			default:
				return false;
		}
	}

	private static string LocalAddresses()
	{
		var addresses = IP.GetLocalAddresses()
			.Where(address => address.Contains('.') && !address.StartsWith("127."))
			.Distinct()
			.ToArray();
		return addresses.Length == 0 ? "127.0.0.1" : string.Join("\n", addresses);
	}
}
