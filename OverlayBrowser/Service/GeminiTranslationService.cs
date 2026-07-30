using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace OverlayBrowser.Service;

/// <summary>
/// Gemini APIを利用してWebページの本文を翻訳する。
/// </summary>
public sealed class GeminiTranslationService
{
    /// <summary>
    /// 通常のページ翻訳で使用するモデル。
    /// </summary>
    public const string DefaultModelName = "gemini-2.5-flash-lite";

    /// <summary>
    /// 混雑時に切り替えられる代替モデル。
    /// </summary>
    public const string AlternativeModelName = "gemini-2.5-flash";
    private const int TranslationBatchCharacterLimit = 2500;
    private const int TranslationBatchSegmentLimit = 12;
    private const int SegmentTranslationOutputTokenLimit = 8192;

    /// <summary>
    /// 翻訳結果に適用する標準の文体と補足方針。
    /// </summary>
    public const string DefaultTranslationPersonalization = "Translate naturally and concisely for a native speaker of the target language. Keep the source meaning faithful. Add a short note only when it is necessary to prevent a misunderstanding, and never add unsupported facts or assumptions.";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(45)
    };

    private readonly GeminiApiKeyStore apiKeyStore;

    /// <summary>
    /// 翻訳サービスを初期化する。
    /// </summary>
    /// <param name="apiKeyStore">Gemini APIキーの保存処理。</param>
    public GeminiTranslationService(GeminiApiKeyStore apiKeyStore)
    {
        this.apiKeyStore = apiKeyStore;
    }

    /// <summary>
    /// ページ本文を指定言語へ翻訳する。
    /// </summary>
    /// <param name="text">翻訳対象のページ本文。</param>
    /// <param name="targetCulture">Windowsで選択されている翻訳先カルチャ。</param>
    /// <param name="personalization">利用者が設定した翻訳結果の文体や補足方針。</param>
    /// <returns>翻訳結果またはエラー内容。</returns>
    public async Task<TranslationResponse> TranslateAsync(
        string text,
        CultureInfo targetCulture,
        string? personalization,
        string modelName = DefaultModelName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return TranslationResponse.Failure("翻訳できるページ本文を取得できませんでした。");
        }

        var apiKey = apiKeyStore.GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return TranslationResponse.Failure("Gemini APIキーが未設定です。［設定］→［Gemini APIキー］から登録してください。");
        }

        try
        {
            var request = new GeminiRequest
            {
                Contents =
                [
                    new GeminiContent
                    {
                        Parts =
                        [
                            new GeminiPart
                            {
                                Text = CreatePrompt(text, targetCulture, personalization)
                            }
                        ]
                    }
                ],
                GenerationConfig = new GeminiGenerationConfig
                {
                    Temperature = 0.1,
                    MaxOutputTokens = 4096
                }
            };
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent");
            httpRequest.Headers.Add("x-goog-api-key", apiKey);
            httpRequest.Content = JsonContent.Create(request);

            using var response = await HttpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                return TranslationResponse.Failure(await CreateErrorMessageAsync(response));
            }

            var payload = await response.Content.ReadFromJsonAsync<GeminiResponse>();
            var translatedText = payload?.Candidates?
                .FirstOrDefault()?
                .Content?
                .Parts?
                .Select(part => part.Text)
                .FirstOrDefault(part => !string.IsNullOrWhiteSpace(part));
            return string.IsNullOrWhiteSpace(translatedText)
                ? TranslationResponse.Failure("Geminiから翻訳結果を取得できませんでした。")
                : TranslationResponse.Success(translatedText.Trim());
        }
        catch (TaskCanceledException)
        {
            return TranslationResponse.Failure("Gemini APIへの接続がタイムアウトしました。ネットワークを確認してください。");
        }
        catch (HttpRequestException)
        {
            return TranslationResponse.Failure("Gemini APIへ接続できませんでした。ネットワークを確認してください。");
        }
        catch (Exception)
        {
            return TranslationResponse.Failure("翻訳中にエラーが発生しました。Gemini APIキーと利用状況を確認してください。");
        }
    }

    /// <summary>
    /// ページ内の文字ノードを翻訳し、元のノードIDに対応する結果を返す。
    /// </summary>
    /// <param name="segments">翻訳対象の文字ノード一覧。</param>
    /// <param name="targetCulture">Windowsで選択されている翻訳先カルチャ。</param>
    /// <param name="personalization">利用者が設定した翻訳結果の文体や補足方針。</param>
    /// <returns>ノードごとの翻訳結果またはエラー内容。</returns>
    public async Task<SegmentTranslationResponse> TranslateSegmentsAsync(
        IReadOnlyList<PageTextSegment> segments,
        CultureInfo targetCulture,
        string? personalization,
        string modelName = DefaultModelName)
    {
        if (segments.Count == 0 || segments.Any(segment => string.IsNullOrWhiteSpace(segment.Text)))
        {
            return SegmentTranslationResponse.Failure("翻訳できるページ本文を取得できませんでした。");
        }

        var apiKey = apiKeyStore.GetApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return SegmentTranslationResponse.Failure("Gemini APIキーが未設定です。［設定］→［Gemini APIキー］から登録してください。");
        }

        var translations = new List<PageTextSegment>();
        foreach (var batch in CreateTranslationBatches(segments))
        {
            var result = await TranslateSegmentBatchAsync(batch, apiKey, targetCulture, personalization, modelName);
            if (!result.IsSuccess)
            {
                return SegmentTranslationResponse.Failure(result.Message);
            }

            translations.AddRange(result.Translations);
        }

        return SegmentTranslationResponse.Success(translations);
    }

    /// <summary>
    /// Geminiへ送る翻訳依頼文を作成する。
    /// </summary>
    /// <param name="text">翻訳対象のページ本文。</param>
    /// <param name="targetCulture">翻訳先カルチャ。</param>
    /// <param name="personalization">翻訳結果の文体や補足方針。</param>
    /// <returns>翻訳依頼文。</returns>
    private static string CreatePrompt(string text, CultureInfo targetCulture, string? personalization)
    {
        var responseStyle = string.IsNullOrWhiteSpace(personalization)
            ? DefaultTranslationPersonalization
            : personalization.Trim();

        return $"Translate the following webpage text into {targetCulture.EnglishName} ({targetCulture.Name}). Preserve headings, bullet points, line breaks, URLs, proper names, game item names, and numbers. Follow this response preference when it does not conflict with faithful translation: {responseStyle}\n\nWebpage text:\n{text}";
    }

    /// <summary>
    /// 指定した文字数を超えない翻訳単位へページ本文を分割する。
    /// </summary>
    /// <param name="segments">分割対象の文字ノード一覧。</param>
    /// <returns>Geminiへ送信する翻訳単位。</returns>
    private static IEnumerable<List<PageTextSegment>> CreateTranslationBatches(IReadOnlyList<PageTextSegment> segments)
    {
        var batch = new List<PageTextSegment>();
        var characterCount = 0;
        foreach (var segment in segments)
        {
            if (batch.Count > 0 &&
                (characterCount + segment.Text.Length > TranslationBatchCharacterLimit ||
                 batch.Count >= TranslationBatchSegmentLimit))
            {
                yield return batch;
                batch = [];
                characterCount = 0;
            }

            batch.Add(segment);
            characterCount += segment.Text.Length;
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }

    /// <summary>
    /// 1つの翻訳単位をGeminiへ送り、ノードIDを維持した翻訳結果を受け取る。
    /// </summary>
    /// <param name="segments">翻訳対象の文字ノード一覧。</param>
    /// <param name="apiKey">Gemini APIキー。</param>
    /// <param name="targetCulture">翻訳先カルチャ。</param>
    /// <param name="personalization">翻訳結果の文体や補足方針。</param>
    /// <param name="modelName">使用するGeminiモデル名。</param>
    /// <returns>ノードごとの翻訳結果またはエラー内容。</returns>
    private async Task<SegmentTranslationResponse> TranslateSegmentBatchAsync(
        IReadOnlyList<PageTextSegment> segments,
        string apiKey,
        CultureInfo targetCulture,
        string? personalization,
        string modelName)
    {
        try
        {
            var request = new GeminiRequest
            {
                Contents =
                [
                    new GeminiContent
                    {
                        Parts =
                        [
                            new GeminiPart
                            {
                                Text = CreateSegmentPrompt(segments, targetCulture, personalization)
                            }
                        ]
                    }
                ],
                GenerationConfig = new GeminiGenerationConfig
                {
                    Temperature = 0.1,
                    MaxOutputTokens = SegmentTranslationOutputTokenLimit,
                    ResponseMimeType = "application/json",
                    ResponseSchema = GeminiResponseFormat.CreatePageTextSegmentArray()
                }
            };
            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent");
            httpRequest.Headers.Add("x-goog-api-key", apiKey);
            httpRequest.Content = JsonContent.Create(request);

            using var response = await HttpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                return SegmentTranslationResponse.Failure(await CreateErrorMessageAsync(response));
            }

            var payload = await response.Content.ReadFromJsonAsync<GeminiResponse>();
            var resultText = payload?.Candidates?
                .FirstOrDefault()?
                .Content?
                .Parts?
                .Select(part => part.Text)
                .FirstOrDefault(part => !string.IsNullOrWhiteSpace(part));
            if (string.IsNullOrWhiteSpace(resultText))
            {
                return SegmentTranslationResponse.Failure("Geminiから翻訳結果を取得できませんでした。");
            }

            var translations = DeserializeSegmentTranslations(resultText);
            if (translations is null || translations.Count == 0)
            {
                return SegmentTranslationResponse.Failure("Geminiの翻訳結果をページへ反映できませんでした。");
            }

            var requestedIds = segments.Select(segment => segment.Id).ToHashSet();
            var validTranslations = translations
                .Where(translation => requestedIds.Contains(translation.Id) && !string.IsNullOrWhiteSpace(translation.Text))
                .ToList();
            return validTranslations.Count == 0
                ? SegmentTranslationResponse.Failure("Geminiの翻訳結果をページへ反映できませんでした。")
                : SegmentTranslationResponse.Success(validTranslations);
        }
        catch (TaskCanceledException)
        {
            return SegmentTranslationResponse.Failure("Gemini APIへの接続がタイムアウトしました。ネットワークを確認してください。");
        }
        catch (HttpRequestException)
        {
            return SegmentTranslationResponse.Failure("Gemini APIへ接続できませんでした。ネットワークを確認してください。");
        }
        catch (JsonException)
        {
            return SegmentTranslationResponse.Failure("Geminiの翻訳結果の形式を読み取れませんでした。もう一度試してください。");
        }
        catch (Exception)
        {
            return SegmentTranslationResponse.Failure("翻訳中にエラーが発生しました。Gemini APIキーと利用状況を確認してください。");
        }
    }

    /// <summary>
    /// 文字ノードと対応するIDを維持するためのGemini用指示文を作成する。
    /// </summary>
    /// <param name="segments">翻訳対象の文字ノード一覧。</param>
    /// <param name="targetCulture">翻訳先カルチャ。</param>
    /// <param name="personalization">翻訳結果の文体や補足方針。</param>
    /// <returns>Geminiへ送る翻訳依頼文。</returns>
    private static string CreateSegmentPrompt(
        IReadOnlyList<PageTextSegment> segments,
        CultureInfo targetCulture,
        string? personalization)
    {
        var responseStyle = string.IsNullOrWhiteSpace(personalization)
            ? DefaultTranslationPersonalization
            : personalization.Trim();
        var sourceJson = JsonSerializer.Serialize(segments);

        return $"Translate each text value in the JSON array into {targetCulture.EnglishName} ({targetCulture.Name}). Return only a valid JSON array. Keep every id unchanged, preserve URLs, proper names, game item names, numbers, and line breaks. Do not merge, omit, add, or reorder items. Follow this response preference when it does not conflict with faithful translation: {responseStyle}\n\nJSON input:\n{sourceJson}";
    }

    /// <summary>
    /// Geminiが付けたMarkdownのコードフェンスをJSON解析前に除去する。
    /// </summary>
    /// <param name="text">Geminiから返った文章。</param>
    /// <returns>JSONとして解析する文章。</returns>
    private static string RemoveJsonCodeFence(string text)
    {
        var value = text.Trim();
        if (!value.StartsWith("```", StringComparison.Ordinal))
        {
            return value;
        }

        var firstLineEnd = value.IndexOf('\n');
        var lastFence = value.LastIndexOf("```", StringComparison.Ordinal);
        return firstLineEnd >= 0 && lastFence > firstLineEnd
            ? value[(firstLineEnd + 1)..lastFence].Trim()
            : value;
    }

    /// <summary>
    /// Geminiが返したJSONから文字ノードごとの翻訳結果を読み取る。
    /// </summary>
    /// <param name="resultText">Geminiから返ったJSONまたはJSONを含む文字列。</param>
    /// <returns>ページへ反映する文字ノード一覧。読み取れない場合はnull。</returns>
    /// <exception cref="JsonException">候補内に有効な翻訳JSONが含まれない場合に発生する。</exception>
    private static List<PageTextSegment>? DeserializeSegmentTranslations(string resultText)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        var normalizedText = RemoveJsonCodeFence(resultText);
        var candidates = new[]
        {
            normalizedText,
            ExtractJsonArray(normalizedText)
        }
        .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
        .Distinct(StringComparer.Ordinal);

        JsonException? lastException = null;
        foreach (var candidate in candidates)
        {
            try
            {
                using var document = JsonDocument.Parse(candidate);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    return JsonSerializer.Deserialize<List<PageTextSegment>>(candidate, options);
                }

                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    TryGetTranslationArray(document.RootElement, out var translationsElement))
                {
                    return JsonSerializer.Deserialize<List<PageTextSegment>>(translationsElement.GetRawText(), options);
                }

                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    HasTranslationFields(document.RootElement))
                {
                    var translation = JsonSerializer.Deserialize<PageTextSegment>(candidate, options);
                    return translation is null ? null : [translation];
                }

                if (document.RootElement.ValueKind == JsonValueKind.String)
                {
                    var nestedJson = document.RootElement.GetString();
                    if (!string.IsNullOrWhiteSpace(nestedJson))
                    {
                        return DeserializeSegmentTranslations(nestedJson);
                    }
                }
            }
            catch (JsonException exception)
            {
                lastException = exception;
            }
        }

        throw lastException ?? new JsonException("Gemini translation JSON was empty.");
    }

    /// <summary>
    /// 翻訳結果の配列を大文字小文字に左右されずに取得する。
    /// </summary>
    /// <param name="element">候補JSONのルート要素。</param>
    /// <param name="translationsElement">見つかった翻訳配列。</param>
    /// <returns>翻訳配列が見つかった場合はtrue。</returns>
    private static bool TryGetTranslationArray(JsonElement element, out JsonElement translationsElement)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals("translations", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("results", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("items", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    translationsElement = property.Value;
                    return true;
                }
            }
        }

        translationsElement = default;
        return false;
    }

    /// <summary>
    /// JSONオブジェクトが1件分の翻訳結果か確認する。
    /// </summary>
    /// <param name="element">確認対象のJSON要素。</param>
    /// <returns>idとtextを持つ場合はtrue。</returns>
    private static bool HasTranslationFields(JsonElement element)
    {
        var hasId = false;
        var hasText = false;
        foreach (var property in element.EnumerateObject())
        {
            hasId |= property.Name.Equals("id", StringComparison.OrdinalIgnoreCase);
            hasText |= property.Name.Equals("text", StringComparison.OrdinalIgnoreCase);
        }

        return hasId && hasText;
    }

    /// <summary>
    /// JSONの前後に補足文が付いた場合に、配列部分だけを取り出す。
    /// </summary>
    /// <param name="text">Geminiから返った文字列。</param>
    /// <returns>見つかったJSON配列。配列がない場合は空文字列。</returns>
    private static string ExtractJsonArray(string text)
    {
        var firstBracket = text.IndexOf('[');
        var lastBracket = text.LastIndexOf(']');
        return firstBracket >= 0 && lastBracket > firstBracket
            ? text[firstBracket..(lastBracket + 1)]
            : string.Empty;
    }

    /// <summary>
    /// Gemini APIのエラー内容を利用者向けの文章へ変換する。
    /// </summary>
    /// <param name="response">APIレスポンス。</param>
    /// <returns>エラー内容。</returns>
    private static async Task<string> CreateErrorMessageAsync(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return "Gemini APIキーが無効、または利用権限がありません。キーを確認してください。";
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return "Gemini APIの利用上限に達しました。少し時間を置いてから試してください。";
        }

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            return "Gemini APIが混雑しています。少し待って再試行するか、別のモデルへ切り替えてください。";
        }

        try
        {
            var content = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message) &&
                !string.IsNullOrWhiteSpace(message.GetString()))
            {
                return $"Gemini APIエラー: {message.GetString()}";
            }
        }
        catch (JsonException)
        {
        }

        return $"Gemini APIがエラーを返しました（HTTP {(int)response.StatusCode}）。";
    }

    /// <summary>
    /// 翻訳結果を表す。
    /// </summary>
    /// <param name="IsSuccess">翻訳に成功したかどうか。</param>
    /// <param name="Message">翻訳結果または利用者向けのエラー内容。</param>
    public sealed record TranslationResponse(bool IsSuccess, string Message)
    {
        /// <summary>
        /// 成功した翻訳結果を作成する。
        /// </summary>
        /// <param name="message">翻訳済み文章。</param>
        /// <returns>成功結果。</returns>
        public static TranslationResponse Success(string message) => new(true, message);

        /// <summary>
        /// 失敗した翻訳結果を作成する。
        /// </summary>
        /// <param name="message">エラー内容。</param>
        /// <returns>失敗結果。</returns>
        public static TranslationResponse Failure(string message) => new(false, message);
    }

    /// <summary>
    /// ページ内の文字ノードを表す。
    /// </summary>
    /// <param name="Id">ページ上の文字ノードを識別する番号。</param>
    /// <param name="Text">翻訳前または翻訳後の文章。</param>
    public sealed record PageTextSegment(int Id, string Text);

    /// <summary>
    /// ページ内の文字ノードを翻訳した結果を表す。
    /// </summary>
    /// <param name="IsSuccess">翻訳に成功したかどうか。</param>
    /// <param name="Message">失敗時に利用者へ表示する内容。</param>
    /// <param name="Translations">ノードIDに対応した翻訳結果。</param>
    public sealed record SegmentTranslationResponse(
        bool IsSuccess,
        string Message,
        IReadOnlyList<PageTextSegment> Translations)
    {
        /// <summary>
        /// 成功したノード翻訳結果を作成する。
        /// </summary>
        /// <param name="translations">ページへ反映する翻訳結果。</param>
        /// <returns>成功結果。</returns>
        public static SegmentTranslationResponse Success(IReadOnlyList<PageTextSegment> translations)
            => new(true, string.Empty, translations);

        /// <summary>
        /// 失敗したノード翻訳結果を作成する。
        /// </summary>
        /// <param name="message">利用者へ表示するエラー内容。</param>
        /// <returns>失敗結果。</returns>
        public static SegmentTranslationResponse Failure(string message)
            => new(false, message, []);
    }

    /// <summary>
    /// Gemini APIへ送信する翻訳リクエストを表す。
    /// </summary>
    private sealed class GeminiRequest
    {
        /// <summary>翻訳対象となるコンテンツ。</summary>
        [JsonPropertyName("contents")]
        public required List<GeminiContent> Contents { get; init; }

        /// <summary>翻訳結果の生成条件。</summary>
        [JsonPropertyName("generationConfig")]
        public required GeminiGenerationConfig GenerationConfig { get; init; }
    }

    /// <summary>
    /// Gemini APIへ渡すコンテンツを表す。
    /// </summary>
    private sealed class GeminiContent
    {
        /// <summary>コンテンツを構成する文章部品。</summary>
        [JsonPropertyName("parts")]
        public required List<GeminiPart> Parts { get; init; }
    }

    /// <summary>
    /// Gemini APIへ渡す文章部品を表す。
    /// </summary>
    private sealed class GeminiPart
    {
        /// <summary>翻訳対象の文章。</summary>
        [JsonPropertyName("text")]
        public required string Text { get; init; }
    }

    /// <summary>
    /// Gemini APIの生成条件を表す。
    /// </summary>
    private sealed class GeminiGenerationConfig
    {
        /// <summary>出力の揺れを抑える温度設定。</summary>
        [JsonPropertyName("temperature")]
        public double Temperature { get; init; }

        /// <summary>翻訳結果に許可する最大トークン数。</summary>
        [JsonPropertyName("maxOutputTokens")]
        public int MaxOutputTokens { get; init; }

        /// <summary>JSON応答を要求するMIMEタイプ。</summary>
        [JsonPropertyName("responseMimeType")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ResponseMimeType { get; init; }

        /// <summary>JSON応答に求める形式。</summary>
        [JsonPropertyName("responseSchema")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public GeminiJsonSchema? ResponseSchema { get; init; }
    }

    /// <summary>
    /// Geminiの構造化出力形式を表す。
    /// </summary>
    private static class GeminiResponseFormat
    {
        /// <summary>
        /// ページ内文字ノードの配列を返す形式指定を作成する。
        /// </summary>
        /// <returns>id と text を必須とするJSON配列の形式指定。</returns>
        public static GeminiJsonSchema CreatePageTextSegmentArray()
        {
            return new GeminiJsonSchema
            {
                Type = "array",
                Items = new GeminiJsonSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, GeminiJsonSchema>
                    {
                        ["id"] = new GeminiJsonSchema { Type = "integer" },
                        ["text"] = new GeminiJsonSchema { Type = "string" }
                    },
                    Required = ["id", "text"]
                }
            };
        }
    }

    /// <summary>
    /// Geminiへ渡す最小限のJSONスキーマを表す。
    /// </summary>
    private sealed class GeminiJsonSchema
    {
        /// <summary>JSON値の種類。</summary>
        [JsonPropertyName("type")]
        public required string Type { get; init; }

        /// <summary>配列要素のスキーマ。</summary>
        [JsonPropertyName("items")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public GeminiJsonSchema? Items { get; init; }

        /// <summary>オブジェクトのプロパティ定義。</summary>
        [JsonPropertyName("properties")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, GeminiJsonSchema>? Properties { get; init; }

        /// <summary>必須プロパティ一覧。</summary>
        [JsonPropertyName("required")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Required { get; init; }
    }

    /// <summary>
    /// Gemini APIから返る翻訳レスポンスを表す。
    /// </summary>
    private sealed class GeminiResponse
    {
        /// <summary>生成された候補一覧。</summary>
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate>? Candidates { get; init; }
    }

    /// <summary>
    /// Gemini APIが返す生成候補を表す。
    /// </summary>
    private sealed class GeminiCandidate
    {
        /// <summary>生成されたコンテンツ。</summary>
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; init; }
    }
}
