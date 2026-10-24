using System.Security.Cryptography.X509Certificates;
using SiagroB1.Application.Services.Reinf;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Certificates;
using SiagroB1.Fiscal.Reinf;
using SiagroB1.Fiscal.Tests.Support;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Grava os lotes enviados e devolve resultados programados (padrão: recebido, código 1).</summary>
public sealed class FakeReinfClient : IReinfClient
{
    public List<string> SentXml { get; } = [];
    public List<string> QueriedProtocols { get; } = [];
    public Queue<Func<ReinfBatchResult>> SendResults { get; } = new();
    public Queue<Func<ReinfBatchResult>> QueryResults { get; } = new();

    /// <summary>Atraso artificial no envio (para testar transmissões concorrentes).</summary>
    public TimeSpan SendDelay { get; set; }

    public async Task<ReinfBatchResult> SendAsync(string batchXml, X509Certificate2 cert, ReinfEnvironment env, CancellationToken ct)
    {
        lock (SentXml) SentXml.Add(batchXml);
        if (SendDelay > TimeSpan.Zero) await Task.Delay(SendDelay, ct);
        lock (SendResults)
            return SendResults.Count > 0
                ? SendResults.Dequeue()()
                : new ReinfBatchResult(1, "Lote recebido", $"PROT-{SentXml.Count}", [], [], "<retorno/>");
    }

    public Task<ReinfBatchResult> QueryAsync(string protocol, X509Certificate2 cert, ReinfEnvironment env, CancellationToken ct)
    {
        QueriedProtocols.Add(protocol);
        return Task.FromResult(QueryResults.Dequeue()());
    }
}

/// <summary>Certificado de teste (CNPJ 12345678000195).</summary>
public sealed class FakeReinfCertificateProvider : IReinfCertificateProvider
{
    public Task<X509Certificate2> GetAsync(string taxIdRoot, CancellationToken ct = default) =>
        Task.FromResult(CertificateLoader.Load(TestCertificates.CreatePfx(), TestCertificates.Password));
}
