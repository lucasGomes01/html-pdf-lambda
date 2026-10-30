import chromium from "@sparticuz/chromium";
import puppeteer from "puppeteer-core";
import { S3Client, GetObjectCommand, PutObjectCommand, DeleteObjectCommand } from "@aws-sdk/client-s3";
import path from "node:path";

const s3 = new S3Client({});

// Evento esperado: { "key": "DEV/Cliente/arquivo.html", "bucket": "opcional" }
// Retorno:         { "bucket": "...", "pdfKey": "DEV/Cliente/arquivo.pdf" }
// Gera o PDF na mesma pasta e apaga o HTML original.
export const handler = async (event) => {
  const bucket = event?.bucket ?? process.env.BUCKET;
  const key = event?.key;

  if (!bucket) throw new Error("Bucket não informado (evento 'bucket' ou variável BUCKET).");
  if (!key) throw new Error("Informe a 'key' do HTML no evento.");

  const objeto = await s3.send(new GetObjectCommand({ Bucket: bucket, Key: key }));
  const html = await objeto.Body.transformToString("utf-8");

  const browser = await puppeteer.launch({
    args: await puppeteer.defaultArgs({ args: chromium.args, headless: "shell" }),
    executablePath: await chromium.executablePath(),
    headless: "shell",
  });

  let pdf;
  try {
    const page = await browser.newPage();
    await page.setContent(html, { waitUntil: "networkidle0", timeout: 20000 });
    pdf = await page.pdf({ format: "A4", printBackground: true });
  } finally {
    await browser.close();
  }

  // PDF na mesma pasta do HTML: DEV/Cliente/arquivo.html -> DEV/Cliente/arquivo.pdf.
  // Com a variável PREFIXO_PDF definida, todos os PDFs vão para essa pasta.
  const nomePdf = `${path.posix.basename(key, path.posix.extname(key))}.pdf`;
  const pasta = process.env.PREFIXO_PDF ?? `${path.posix.dirname(key)}/`;
  const pdfKey = pasta === "./" ? nomePdf : `${pasta}${nomePdf}`;

  await s3.send(new PutObjectCommand({
    Bucket: bucket,
    Key: pdfKey,
    Body: pdf,
    ContentType: "application/pdf",
  }));

  // Só apaga o HTML depois que o PDF foi gravado; se algo falhar antes, o HTML fica.
  await s3.send(new DeleteObjectCommand({ Bucket: bucket, Key: key }));

  return { bucket, pdfKey };
};
