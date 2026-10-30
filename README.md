# ImagemParaS3

Console .NET 8 que envia imagens e documentos HTML para um bucket S3. Para HTML,
chama uma AWS Lambda (Node.js + Chromium headless) que converte o arquivo em PDF.

## Configuração

1. **Credenciais AWS** (uma vez só):
   ```
   aws configure
   ```
   ou defina `AWS_ACCESS_KEY_ID` e `AWS_SECRET_ACCESS_KEY`.
   O usuário IAM precisa de `s3:PutObject` e `s3:GetObject` no bucket
   (e de `lambda:InvokeFunction` na função, para gerar PDF).

2. **Configuração local**: o `appsettings.json` versionado só tem campos vazios.
   Crie um `appsettings.local.json` (já está no `.gitignore`) com os seus valores:
   ```json
   {
     "Aws": {
       "BucketName": "<seu-bucket>",
       "Region": "us-east-1",
       "PastaPadrao": "DEV/Cliente",
       "FuncaoPdf": "<nome-da-funcao>"
     },
     "PastaImagens": "C:\\caminho\\da\\pasta"
   }
   ```
   Também dá para usar variáveis de ambiente, ex.: `Aws__BucketName`.

| Chave | O que é |
|---|---|
| `Aws:BucketName` | Nome do bucket S3 |
| `Aws:Region` | Região do bucket e da Lambda |
| `Aws:PastaPadrao` | Pasta no S3 sugerida ao enviar (Enter aceita) |
| `Aws:FuncaoPdf` | Nome da Lambda que converte HTML em PDF |
| `Aws:ValidadeUrlHoras` | Validade da URL assinada (padrão 24) |
| `PastaImagens` | Pasta local de onde o arquivo mais recente é lido |

## Uso

```
dotnet run                            # arquivo mais recente da PastaImagens
dotnet run -- "C:\docs\relatorio.html" # arquivo específico
dotnet run -- "C:\docs"                # arquivo mais recente dessa pasta
```

O console pergunta a pasta no S3 e envia o arquivo com o nome original
(`DEV/Cliente/relatorio.html`). Se for `.html`/`.htm`, pergunta se pode rodar a
Lambda; com `s`, ela gera o PDF na mesma pasta, apaga o HTML e o console imprime
a URL assinada do PDF. Se der erro antes de gravar o PDF, o HTML continua lá.

```
Pasta no S3 (ex.: DEV/Cliente) [DEV/Cliente]: DEV/Cliente
Enviando relatorio.html para s3://<seu-bucket>/DEV/Cliente/relatorio.html ...
Upload concluído!
Posso rodar a Lambda <nome-da-funcao> para gerar o PDF? (s/n): s
PDF gerado: s3://<seu-bucket>/DEV/Cliente/relatorio.pdf
```

Enviar um arquivo com o mesmo nome na mesma pasta substitui o anterior.

Imagens e CSS do HTML precisam ser URLs absolutas ou estar embutidos
(CSS inline, imagens em base64): caminhos relativos não existem dentro da Lambda.

## Lambda HTML → PDF

Código em `lambda-html-pdf/` (Node.js 22, `@sparticuz/chromium` 153 +
`puppeteer-core` 25.11). Evento: `{ "key": "pasta/arquivo.html" }`.
Retorno: `{ "bucket": "...", "pdfKey": "pasta/arquivo.pdf" }`.

### Criar a função na AWS (uma vez só)

Nos exemplos abaixo, troque `<seu-bucket>`, `<id-da-conta>`, `<regiao>` e
`<nome-da-funcao>` pelos seus valores.

1. **Gerar o pacote**
   ```
   powershell -ExecutionPolicy Bypass -File .\lambda-html-pdf\empacotar.ps1
   ```
   Gera `lambda-html-pdf\lambda.zip` (~73 MB). Passa de 50 MB, então vai via S3.

2. **Subir o zip para o S3**: console S3 → bucket → pasta `deploy/` → *Upload* →
   `lambda.zip`. Copie a *Object URL*.

3. **Criar a função**: console Lambda → *Create function* → *Author from scratch*
   - Runtime: **Node.js 22.x**
   - Architecture: **x86_64** (o Chromium do pacote não roda em arm64)
   - Permissions: *Create a new role with basic Lambda permissions*

4. **Código**: aba *Code* → *Upload from* → *Amazon S3 location* → cole a Object
   URL (modo *Cópia*). O handler deve ser `index.handler`.

5. **Configuration → General configuration**: Memory **2048 MB**, Timeout **1 min**.

6. **Configuration → Environment variables**: `BUCKET` = `<seu-bucket>`.

7. **Permissão da Lambda no S3** (inline policy na role da função):
   ```json
   {
     "Version": "2012-10-17",
     "Statement": [
       { "Effect": "Allow", "Action": ["s3:GetObject", "s3:PutObject", "s3:DeleteObject"],
         "Resource": "arn:aws:s3:::<seu-bucket>/*" }
     ]
   }
   ```

8. **Permissão do usuário do console para chamar a Lambda** (inline policy no
   usuário IAM):
   ```json
   {
     "Version": "2012-10-17",
     "Statement": [
       { "Effect": "Allow", "Action": "lambda:InvokeFunction",
         "Resource": "arn:aws:lambda:<regiao>:<id-da-conta>:function:<nome-da-funcao>" }
     ]
   }
   ```

9. **Testar** (opcional): suba um `.html` no bucket e, na aba *Test* da Lambda,
   use `{ "key": "DEV/Cliente/seu-arquivo.html" }`. O PDF aparece na mesma pasta.

Para atualizar o código: rode o passo 1, suba o zip de novo e repita o passo 4.
