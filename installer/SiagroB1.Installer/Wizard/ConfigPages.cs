using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SiagroB1.Installer.Core.Settings;

namespace SiagroB1.Installer.Wizard;

public enum FieldKind { Text, Secret, Bool, Number }

public sealed class EditableField(ServiceKind service, string path, string label, FieldKind kind, string original) : ObservableObject
{
    private string _value = original;

    public ServiceKind Service { get; } = service;
    public string Path { get; } = path;
    public string Key => Path;
    public string Label { get; } = label;
    public FieldKind Kind { get; } = kind;
    public string Original { get; } = original;
    public bool IsSecret => Kind == FieldKind.Secret;
    public bool IsBool => Kind == FieldKind.Bool;
    public bool IsText => Kind is FieldKind.Text or FieldKind.Number;

    public string Value { get => _value; set { Set(ref _value, value); OnPropertyChanged(nameof(BoolValue)); } }
    public bool BoolValue { get => bool.TryParse(Value, out var b) && b; set => Value = value ? "true" : "false"; }

    public bool IsChanged => !string.Equals(Value, Original, StringComparison.Ordinal);

    public JsonNode? ToNode() => Kind switch
    {
        FieldKind.Bool => JsonValue.Create(BoolValue),
        FieldKind.Number when int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => JsonValue.Create(i),
        FieldKind.Number when long.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => JsonValue.Create(n),
        FieldKind.Number when decimal.TryParse(Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => JsonValue.Create(d),
        _ => JsonValue.Create(Value)
    };

    public static string Text(JsonNode? node) => node switch
    {
        null => "",
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        JsonValue v when v.GetValueKind() is JsonValueKind.True or JsonValueKind.False => v.GetValue<bool>() ? "true" : "false",
        _ => node.ToJsonString()
    };

    public static FieldKind KindOf(JsonNode? node, string path) => node switch
    {
        JsonValue v when v.GetValueKind() is JsonValueKind.True or JsonValueKind.False => FieldKind.Bool,
        JsonValue v when v.GetValueKind() == JsonValueKind.Number => FieldKind.Number,
        _ => SecretMasker.MaskValue(path, "x") == "***" ? FieldKind.Secret : FieldKind.Text
    };
}

internal static class JsonPath
{
    public static JsonNode? Get(JsonObject? root, string path)
    {
        JsonNode? current = root;
        foreach (var segment in path.Split(':'))
        {
            if (current is not JsonObject obj) return null;
            var key = obj.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, segment, StringComparison.OrdinalIgnoreCase));
            if (key is null) return null;
            current = obj[key];
        }
        return current;
    }
}

public sealed class IntegrationsPageViewModel(WizardState state, WizardServices services) : PageViewModel(state, services)
{
    private static readonly (ServiceKind Service, string Path, string Label, FieldKind Kind)[] Definitions =
    [
        (ServiceKind.Gateway, "Email:Enabled", "E-mail (recuperação de senha) ativo", FieldKind.Bool),
        (ServiceKind.Gateway, "Email:Host", "Servidor SMTP", FieldKind.Text),
        (ServiceKind.Gateway, "Email:Port", "Porta SMTP", FieldKind.Number),
        (ServiceKind.Gateway, "Email:UseStartTls", "Usar STARTTLS", FieldKind.Bool),
        (ServiceKind.Gateway, "Email:User", "Usuário SMTP", FieldKind.Text),
        (ServiceKind.Gateway, "Email:Password", "Senha SMTP", FieldKind.Secret),
        (ServiceKind.Gateway, "Email:FromAddress", "E-mail do remetente", FieldKind.Text),
        (ServiceKind.Gateway, "Email:FromName", "Nome do remetente", FieldKind.Text),
        (ServiceKind.Web, "Notifications:WhatsApp:Enabled", "WhatsApp ativo", FieldKind.Bool),
        (ServiceKind.Web, "Notifications:WhatsApp:InstanceId", "WhatsApp: instância", FieldKind.Text),
        (ServiceKind.Web, "Notifications:WhatsApp:Token", "WhatsApp: token", FieldKind.Secret),
        (ServiceKind.Web, "Notifications:WhatsApp:ClientToken", "WhatsApp: client token", FieldKind.Secret),
        (ServiceKind.Web, "Signature:Enabled", "Assinatura eletrônica (D4Sign) ativa", FieldKind.Bool),
        (ServiceKind.Web, "Signature:D4Sign:BaseUrl", "D4Sign: URL (sandbox ou produção)", FieldKind.Text),
        (ServiceKind.Web, "Signature:D4Sign:TokenApi", "D4Sign: token da API", FieldKind.Secret),
        (ServiceKind.Web, "Signature:D4Sign:CryptKey", "D4Sign: crypt key", FieldKind.Secret),
        (ServiceKind.Web, "Signature:D4Sign:SafeId", "D4Sign: cofre", FieldKind.Text),
        (ServiceKind.Web, "Signature:D4Sign:WebhookBaseUrl", "D4Sign: URL do webhook", FieldKind.Text),
        (ServiceKind.Web, "Signature:D4Sign:WebhookSecret", "D4Sign: segredo do webhook", FieldKind.Secret),
        (ServiceKind.Web, "Signature:Pdf:ChromiumPath", "Caminho do Chromium (PDF)", FieldKind.Text),
        (ServiceKind.Web, "Nfe:CertificateKey", "NF-e: chave do certificado (base64 de 32 bytes)", FieldKind.Secret),
        (ServiceKind.Web, "Nfe:TechnicalResponsible:Cnpj", "NF-e: CNPJ do responsável técnico", FieldKind.Text),
        (ServiceKind.Web, "Nfe:TechnicalResponsible:Contact", "NF-e: contato do responsável técnico", FieldKind.Text),
        (ServiceKind.Web, "Nfe:TechnicalResponsible:Email", "NF-e: e-mail do responsável técnico", FieldKind.Text),
        (ServiceKind.Web, "Nfe:TechnicalResponsible:Phone", "NF-e: telefone do responsável técnico", FieldKind.Text),
        (ServiceKind.Web, "Reinf:SoftwareHouse:Cnpj", "Reinf: CNPJ da software house", FieldKind.Text),
        (ServiceKind.Web, "Reinf:SoftwareHouse:Name", "Reinf: nome da software house", FieldKind.Text),
        (ServiceKind.Web, "Reinf:SoftwareHouse:Contact", "Reinf: contato", FieldKind.Text),
        (ServiceKind.Web, "Reinf:SoftwareHouse:Phone", "Reinf: telefone", FieldKind.Text),
        (ServiceKind.Web, "Reinf:SoftwareHouse:Email", "Reinf: e-mail", FieldKind.Text)
    ];

    public override string Title => "Integrações (opcional)";
    public override string Subtitle => "Deixe como está o que não usa. Só o que você alterar é gravado.";

    public ObservableCollection<EditableField> Fields { get; } = [];

    public override void OnEnter()
    {
        if (Fields.Count > 0) return;
        foreach (var (service, path, label, kind) in Definitions)
        {
            var node = State.Settings.Integrations.TryGetValue(path, out var edited) ? edited
                : JsonPath.Get(State.ClientFiles[service], path) ?? JsonPath.Get(State.PayloadDefaults(service), path);
            Fields.Add(new EditableField(service, path, label, kind, EditableField.Text(node)));
        }
    }

    public override IReadOnlyList<string> Validate() =>
        Fields.Where(f => f.Kind == FieldKind.Number && f.Value.Length > 0 && !decimal.TryParse(f.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
              .Select(f => $"\"{f.Label}\" deve ser um número.")
              .ToList();

    public override void OnLeave()
    {
        foreach (var field in Fields.Where(f => f.IsChanged))
        {
            State.Settings.Integrations[field.Path] = field.ToNode();
        }
    }
}

public sealed class NewKeysPageViewModel(WizardState state, WizardServices services) : PageViewModel(state, services)
{
    public override string Title => "Configurações novas desta versão";
    public override string Subtitle => "Estas chaves não existem na configuração atual. Se não mexer, vale o valor padrão.";

    public ObservableCollection<EditableField> Items { get; } = [];

    public override bool IsVisible() => Items.Count > 0;

    public override void OnEnter()
    {
        Items.Clear();
        foreach (var kind in Enum.GetValues<ServiceKind>())
        {
            var client = State.ClientFiles[kind];
            var defaults = State.PayloadDefaults(kind);
            if (client is null || defaults is null) continue;

            var edits = ServiceSettingsMapper.ToEdits(kind, State.Settings);
            var result = AppSettingsMerger.Merge(defaults, client, edits, ServiceSettingsMapper.DropPaths(kind));
            foreach (var key in result.NewKeys.Where(k => !k.StartsWith("Logging:", StringComparison.OrdinalIgnoreCase)))
            {
                var node = JsonPath.Get(defaults, key);
                var current = State.Settings.ServiceEdits[kind].TryGetValue(key, out var edited) ? edited : node;
                Items.Add(new EditableField(kind, key, $"{kind}: {key}", EditableField.KindOf(node, key), EditableField.Text(node))
                {
                    Value = EditableField.Text(current)
                });
            }
        }
    }

    public override IReadOnlyList<string> Validate() => [];

    public override void OnLeave()
    {
        foreach (var item in Items)
        {
            if (item.IsChanged)
                State.Settings.ServiceEdits[item.Service][item.Key] = item.ToNode();
            else
                State.Settings.ServiceEdits[item.Service].Remove(item.Key);
        }
    }
}
