using System.Text.Json;
using Amazon;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    // Valores reais (bucket, função, pastas) ficam fora do git, em appsettings.local.json
    // ou em variáveis de ambiente (ex.: Aws__BucketName).
    .AddJsonFile("appsettings.local.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var bucket = Valor("Aws:BucketName") ?? throw new InvalidOperationException("Aws:BucketName não configurado (veja appsettings.local.json).");
var regiao = RegionEndpoint.GetBySystemName(Valor("Aws:Region") ?? "us-east-1");
var pastaPadrao = Valor("Aws:PastaPadrao") ?? "";
var funcaoPdf = Valor("Aws:FuncaoPdf") ?? "html-para-pdf";
var validadeHoras = int.TryParse(config["Aws:ValidadeUrlHoras"], out var h) ? h : 24;

// Uso: dotnet run -- [caminho de uma imagem/HTML ou pasta]
// Sem argumento, pega o arquivo mais recente da pasta configurada.
// Imagem: só envia para o S3. HTML: envia e, se você confirmar, chama a Lambda que gera o PDF.
var caminho = args.Length > 0 ? args[0] : Valor("PastaImagens") ?? ".";
var arquivo = EncontrarArquivo(caminho);

if (arquivo is null)
{
    Console.WriteLine($"Nenhuma imagem ou HTML encontrado em: {caminho}");
    return 1;
}

Console.WriteLine($"Arquivo: {arquivo}");

// Pasta de destino no S3, ex.: DEV/Cliente -> DEV/Cliente/arquivo.html
var pasta = NormalizarPasta(Perguntar("Pasta no S3 (ex.: DEV/Cliente)", pastaPadrao));
var nome = Path.GetFileName(arquivo);
var chave = pasta.Length > 0 ? $"{pasta}/{nome}" : nome;

// Credenciais: usa o perfil padrão da AWS (aws configure) ou as variáveis
// AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY. Nada de chave no código.
using var s3 = new AmazonS3Client(regiao);

Console.WriteLine($"Enviando {nome} para s3://{bucket}/{chave} ...");

await s3.PutObjectAsync(new PutObjectRequest
{
    BucketName = bucket,
    Key = chave,
    FilePath = arquivo,
    ContentType = ObterContentType(arquivo)
});

Console.WriteLine("Upload concluído!");

var ehHtml = Path.GetExtension(arquivo).ToLowerInvariant() is ".html" or ".htm";

if (ehHtml && Confirmar($"Posso rodar a Lambda {funcaoPdf} para gerar o PDF?"))
{
    Console.WriteLine($"Chamando a Lambda {funcaoPdf} ...");

    using var lambda = new AmazonLambdaClient(regiao);
    var resposta = await lambda.InvokeAsync(new InvokeRequest
    {
        FunctionName = funcaoPdf,
        Payload = JsonSerializer.Serialize(new { bucket, key = chave })
    });

    using var leitor = new StreamReader(resposta.Payload);
    var json = await leitor.ReadToEndAsync();

    // Erro dentro da Lambda volta com status 200, mas com FunctionError preenchido.
    if (!string.IsNullOrEmpty(resposta.FunctionError))
    {
        Console.WriteLine($"A Lambda falhou: {json}");
        return 1;
    }

    chave = JsonDocument.Parse(json).RootElement.GetProperty("pdfKey").GetString()!;
    Console.WriteLine($"PDF gerado: s3://{bucket}/{chave}");
}

// URL temporária assinada: funciona mesmo com o bucket privado (padrão da AWS).
var urlAssinada = await s3.GetPreSignedURLAsync(new GetPreSignedUrlRequest
{
    BucketName = bucket,
    Key = chave,
    Verb = HttpVerb.GET,
    Expires = DateTime.UtcNow.AddHours(validadeHoras)
});

// URL fixa: só abre no navegador se o bucket/objeto tiver leitura pública.
var urlFixa = $"https://{bucket}.s3.{regiao.SystemName}.amazonaws.com/{Uri.EscapeDataString(chave).Replace("%2F", "/")}";

Console.WriteLine();
Console.WriteLine($"Key no S3:              {chave}");
Console.WriteLine($"URL assinada ({validadeHoras}h): {urlAssinada}");
Console.WriteLine($"URL fixa (se público):   {urlFixa}");
return 0;

// Campo vazio no appsettings.json conta como "não configurado".
string? Valor(string chave) => string.IsNullOrWhiteSpace(config[chave]) ? null : config[chave];

static string Perguntar(string texto, string padrao)
{
    Console.Write(padrao.Length > 0 ? $"{texto} [{padrao}]: " : $"{texto}: ");
    var resposta = Console.ReadLine()?.Trim();
    return string.IsNullOrEmpty(resposta) ? padrao : resposta;
}

static bool Confirmar(string texto)
{
    Console.Write($"{texto} (s/n): ");
    var resposta = Console.ReadLine()?.Trim().ToLowerInvariant();
    return resposta is "s" or "sim" or "y" or "yes";
}

// Aceita "DEV\Cliente", "/DEV/Cliente/" etc. e devolve "DEV/Cliente".
static string NormalizarPasta(string pasta) =>
    string.Join('/', pasta.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

static string? EncontrarArquivo(string caminho)
{
    string[] extensoes = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".html", ".htm"];

    if (File.Exists(caminho))
        return caminho;

    if (!Directory.Exists(caminho))
        return null;

    return new DirectoryInfo(caminho)
        .EnumerateFiles()
        .Where(f => extensoes.Contains(f.Extension.ToLowerInvariant()))
        .OrderByDescending(f => f.LastWriteTimeUtc)
        .Select(f => f.FullName)
        .FirstOrDefault();
}

static string ObterContentType(string arquivo) => Path.GetExtension(arquivo).ToLowerInvariant() switch
{
    ".jpg" or ".jpeg" => "image/jpeg",
    ".png" => "image/png",
    ".gif" => "image/gif",
    ".webp" => "image/webp",
    ".bmp" => "image/bmp",
    ".html" or ".htm" => "text/html; charset=utf-8",
    _ => "application/octet-stream"
};
