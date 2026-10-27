using SiagroB1.Installer.Core.Settings;

// Usado pelo build-installer.ps1: sanitize <entrada> <saída>
if (args is not ["sanitize", var input, var output])
{
    Console.Error.WriteLine("Uso: SiagroB1.Installer.Tools sanitize <appsettings.json> <saída>");
    return 2;
}

JsonFile.Save(output, DefaultsSanitizer.Sanitize(JsonFile.Load(input)));
Console.WriteLine($"Sanitizado: {output}");
return 0;
