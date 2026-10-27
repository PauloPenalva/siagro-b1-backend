namespace SiagroB1.Installer.Wizard;

public abstract class PageViewModel(WizardState state, WizardServices services) : ObservableObject
{
    protected WizardState State { get; } = state;
    protected WizardServices Services { get; } = services;

    public abstract string Title { get; }
    public virtual string Subtitle => "";

    /// <summary>Mensagens em pt-BR que impedem avançar; vazio = pode avançar.</summary>
    public abstract IReadOnlyList<string> Validate();

    public virtual bool IsVisible() => true;
    public virtual void OnEnter() { }
    public virtual Task OnEnterAsync() => Task.CompletedTask;
    public virtual void OnLeave() { }
}
