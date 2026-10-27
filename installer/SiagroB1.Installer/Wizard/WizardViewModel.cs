namespace SiagroB1.Installer.Wizard;

public sealed class WizardViewModel : ObservableObject
{
    private readonly List<PageViewModel> _pages;
    private readonly SummaryPageViewModel _summary;
    private readonly ProgressPageViewModel _progress;
    private readonly DonePageViewModel _done;
    private int _index;
    private string _errors = "";
    private bool _busy;

    public WizardViewModel(WizardState state, WizardServices services, Action close)
    {
        _summary = new SummaryPageViewModel(state, services);
        _progress = new ProgressPageViewModel(state, services);
        _done = new DonePageViewModel(state, services, _progress);
        _pages =
        [
            new WelcomePageViewModel(state, services),
            new FolderPageViewModel(state, services),
            new DatabasePageViewModel(state, services),
            new ErpPageViewModel(state, services),
            new CompanyPortsPageViewModel(state, services),
            new IntegrationsPageViewModel(state, services),
            new NewKeysPageViewModel(state, services),
            _summary,
            _progress,
            _done
        ];

        NextCommand = new AsyncRelayCommand(NextAsync, () => !_busy);
        BackCommand = new RelayCommand(Back, () => !_busy && _index > 0 && Current != _progress && Current != _done);
        CancelCommand = new RelayCommand(close, () => !_busy || Current == _done);
        _pages[0].OnEnter();
    }

    public PageViewModel Current => _pages[_index];
    public string Errors { get => _errors; private set => Set(ref _errors, value); }
    public string NextText => Current == _summary ? "Instalar" : Current == _done ? "Concluir" : "Avançar";
    public int Step => _pages.Take(_index + 1).Count(p => p != _progress && p != _done);
    public AsyncRelayCommand NextCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand CancelCommand { get; }
    public Action? Close { get; set; }

    private async Task NextAsync()
    {
        if (Current == _done)
        {
            CancelCommand.Execute(null);
            return;
        }

        var errors = Current.Validate();
        if (errors.Count == 0)
        {
            _busy = true;
            try
            {
                await Task.Yield();
                Current.OnLeave();
            }
            finally
            {
                _busy = false;
            }
            errors = Current.Validate();
        }
        if (errors.Count > 0)
        {
            Errors = string.Join(Environment.NewLine, errors);
            return;
        }
        Errors = "";

        var next = _index + 1;
        _pages[next].OnEnter();
        while (!_pages[next].IsVisible())
        {
            next++;
            _pages[next].OnEnter();
        }
        Go(next);

        _busy = Current == _progress;
        try
        {
            await Current.OnEnterAsync();
        }
        finally
        {
            _busy = false;
        }

        if (Current == _progress)
        {
            _done.OnEnter();
            Go(_index + 1);
        }
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    private void Back()
    {
        Errors = "";
        var previous = _index - 1;
        while (previous > 0 && !_pages[previous].IsVisible())
        {
            previous--;
        }
        Go(previous);
    }

    private void Go(int index)
    {
        _index = index;
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(NextText));
        OnPropertyChanged(nameof(Step));
    }
}
