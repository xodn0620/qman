using System;
using System.Text;

string obfuscationKey = "QManDefaultKey2026";
string llmKey = "a4983324-398c-4ce1-a601-8827bfb47ef3";
string embeddingKey = "11de8667-b34f-47a9-b962-cae7e9748255";

var keyBytes = Encoding.UTF8.GetBytes(obfuscationKey);

// LLM 키 난독화
var llmBytes = Encoding.UTF8.GetBytes(llmKey);
var llmResult = new byte[llmBytes.Length];
for (int i = 0; i < llmBytes.Length; i++)
{
    llmResult[i] = (byte)(llmBytes[i] ^ keyBytes[i % keyBytes.Length]);
}
var llmObfuscated = Convert.ToBase64String(llmResult);

// 임베딩 키 난독화
var embBytes = Encoding.UTF8.GetBytes(embeddingKey);
var embResult = new byte[embBytes.Length];
for (int i = 0; i < embBytes.Length; i++)
{
    embResult[i] = (byte)(embBytes[i] ^ keyBytes[i % keyBytes.Length]);
}
var embObfuscated = Convert.ToBase64String(embResult);

Console.WriteLine($"LLM 키 난독화: {llmObfuscated}");
Console.WriteLine($"임베딩 키 난독화: {embObfuscated}");

// 검증: 복호화
var llmDecrypted = new byte[llmResult.Length];
for (int i = 0; i < llmResult.Length; i++)
{
    llmDecrypted[i] = (byte)(llmResult[i] ^ keyBytes[i % keyBytes.Length]);
}
var llmPlain = Encoding.UTF8.GetString(llmDecrypted);

var embDecrypted = new byte[embResult.Length];
for (int i = 0; i < embResult.Length; i++)
{
    embDecrypted[i] = (byte)(embResult[i] ^ keyBytes[i % keyBytes.Length]);
}
var embPlain = Encoding.UTF8.GetString(embDecrypted);

Console.WriteLine($"\nLLM 키 복호화: {llmPlain}");
Console.WriteLine($"임베딩 키 복호화: {embPlain}");
