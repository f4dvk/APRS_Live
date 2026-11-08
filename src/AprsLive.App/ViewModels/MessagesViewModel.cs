using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AprsLive.Core.Ax25;
using AprsLive.Core.Messaging;
using AprsLive.Core.Settings;

namespace AprsLive.App.ViewModels;

/// <summary>
/// Backs the messages panel: one conversation per peer callsign, with a
/// compose box and APRS-style ack tracking (resend a few times, then give
/// up) — the collapsible panel next to the map, rather than a separate root
/// tab, so a conversation stays visible alongside the station it's with.
/// </summary>
public partial class MessagesViewModel : ViewModelBase
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

    private readonly AppSettings _settings;
    private readonly LinkViewModel _linkA;
    private readonly LinkViewModel _linkB;
    private readonly AprsIsViewModel _aprsIs;
    private readonly MessageCenter _center;
    private readonly Dictionary<string, ConversationViewModel> _byPeer = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, System.Timers.Timer> _retryTimers = new();
    private readonly Dictionary<string, int> _retryAttempts = new();
    private int _messageCounter;

    public ObservableCollection<ConversationViewModel> Conversations { get; } = new();

    [ObservableProperty]
    private ConversationViewModel? _selectedConversation;

    [ObservableProperty]
    private string _newPeerCallsign = "";

    [ObservableProperty]
    private string _draftText = "";

    [ObservableProperty]
    private bool _isPanelOpen;

    [ObservableProperty]
    private string? _notification;

    private string? _notificationPeer;

    public bool CanCompose => SelectedConversation is not null;

    public MessagesViewModel(AppSettings settings, LinkViewModel linkA, LinkViewModel linkB, AprsIsViewModel aprsIs, MessageCenter center)
    {
        _settings = settings;
        _linkA = linkA;
        _linkB = linkB;
        _aprsIs = aprsIs;
        _center = center;

        _center.ConversationUpdated += conv => Dispatcher.UIThread.Post(() => SyncConversation(conv));
        _center.IncomingMessage += (conv, msg) => Dispatcher.UIThread.Post(() => OnIncoming(conv, msg));
        _center.AckRequested += (peer, number) => Dispatcher.UIThread.Post(() => _ = SendAckAsync(peer, number));
    }

    partial void OnSelectedConversationChanged(ConversationViewModel? value) => OnPropertyChanged(nameof(CanCompose));

    private ConversationViewModel GetOrCreateRow(string peer)
    {
        if (!_byPeer.TryGetValue(peer, out var row))
        {
            row = new ConversationViewModel(peer);
            _byPeer[peer] = row;
            Conversations.Add(row);
        }
        return row;
    }

    private void SyncConversation(Conversation conversation)
    {
        var row = GetOrCreateRow(conversation.Peer);
        row.SyncFrom(conversation);

        // Move (not clear+rebuild) the most recently active conversation to the
        // front — clearing the bound ObservableCollection would reset the
        // ListBox's SelectedItem to null on every send/receive.
        int index = Conversations.IndexOf(row);
        if (index > 0)
            Conversations.Move(index, 0);
    }

    private void OnIncoming(Conversation conversation, ConversationMessage message)
    {
        if (IsPanelOpen && SelectedConversation?.Peer == conversation.Peer)
            return;

        _notificationPeer = conversation.Peer;
        Notification = $"Nouveau message de {conversation.Peer} : {message.Text}";
    }

    [RelayCommand]
    private void TogglePanel() => IsPanelOpen = !IsPanelOpen;

    public void OpenConversationWith(string peer)
    {
        peer = peer.Trim().ToUpperInvariant();
        if (peer.Length == 0)
            return;

        IsPanelOpen = true;
        SelectedConversation = GetOrCreateRow(peer);

        if (string.Equals(_notificationPeer, peer, StringComparison.OrdinalIgnoreCase))
        {
            Notification = null;
            _notificationPeer = null;
        }
    }

    [RelayCommand]
    private void StartConversation()
    {
        if (string.IsNullOrWhiteSpace(NewPeerCallsign))
            return;

        OpenConversationWith(NewPeerCallsign);
        NewPeerCallsign = "";
    }

    [RelayCommand]
    private void OpenNotification()
    {
        if (_notificationPeer is { } peer)
            OpenConversationWith(peer);
    }

    [RelayCommand]
    private void DismissNotification()
    {
        Notification = null;
        _notificationPeer = null;
    }

    [RelayCommand]
    private async Task Send()
    {
        if (SelectedConversation is null || string.IsNullOrWhiteSpace(DraftText))
            return;

        var s = _settings.Station;
        if (string.IsNullOrWhiteSpace(s.Callsign))
            return;

        string peer = SelectedConversation.Peer;
        string text = DraftText.Trim();
        DraftText = "";

        _messageCounter = (_messageCounter % 99) + 1;
        string number = _messageCounter.ToString("00");

        _center.RecordOutgoing(peer, text, number);
        var frame = Ax25Builder.BuildMessage(s.Callsign, peer, text, s.Destination, s.Path, number);

        bool sent = await SendFrameViaBestLinkAsync(frame);
        if (!sent)
        {
            _center.MarkFailed(peer, number);
            return;
        }

        StartRetryTimer(peer, number, frame);
    }

    private async Task SendAckAsync(string peer, string number)
    {
        var s = _settings.Station;
        if (string.IsNullOrWhiteSpace(s.Callsign))
            return;

        var frame = Ax25Builder.BuildAck(s.Callsign, peer, number, s.Destination, s.Path);
        await SendFrameViaBestLinkAsync(frame);
    }

    /// <summary>Sends on every RF channel with "Émission" on (same rule as the beacon); falls back to APRS-IS if none of them actually sent it.</summary>
    private async Task<bool> SendFrameViaBestLinkAsync(byte[] frame)
    {
        var sentOn = await TxLinks.SendAsync(frame, _linkA, _linkB);
        if (sentOn.Count > 0)
            return true;
        return await _aprsIs.SendRawFrameAsync(frame);
    }

    private void StartRetryTimer(string peer, string number, byte[] frame)
    {
        string key = $"{peer}|{number}";
        StopRetryTimer(key);

        var timer = new System.Timers.Timer(RetryInterval.TotalMilliseconds) { AutoReset = true };
        timer.Elapsed += (_, _) => Dispatcher.UIThread.Post(() => _ = RetryTickAsync(peer, number, frame, key));
        _retryTimers[key] = timer;
        _retryAttempts[key] = 0;
        timer.Start();
    }

    private async Task RetryTickAsync(string peer, string number, byte[] frame, string key)
    {
        if (_center.GetAckState(peer, number) != MessageAckState.Pending)
        {
            StopRetryTimer(key);
            return;
        }

        int attempts = _retryAttempts.GetValueOrDefault(key) + 1;
        _retryAttempts[key] = attempts;
        if (attempts >= MaxRetries)
        {
            _center.MarkFailed(peer, number);
            StopRetryTimer(key);
            return;
        }

        await SendFrameViaBestLinkAsync(frame);
    }

    private void StopRetryTimer(string key)
    {
        if (_retryTimers.TryGetValue(key, out var timer))
        {
            timer.Stop();
            timer.Dispose();
            _retryTimers.Remove(key);
        }
        _retryAttempts.Remove(key);
    }
}
