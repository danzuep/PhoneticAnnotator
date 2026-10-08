using PhoneticAnnotator.Core;

return await RunAsync(args);

static async Task<int> RunAsync(string[] arguments)
{
	if (arguments.Length == 1 && arguments[0] is "--help" or "-h")
	{
		PrintUsage();
		return 0;
	}

	if (!TryParseArguments(arguments, out var options, out var error))
	{
		Console.Error.WriteLine($"Error: {error}");
		PrintUsage();
		return 2;
	}

	try
	{
		var language = new LanguageCode(options["--language"]);
		var text = options["--text"];
		var format = options.GetValueOrDefault("--format", "html");

		ITokenizerStrategy strategy;
		if (options.TryGetValue("--lexicon", out var lexiconPath))
		{
			using var lexiconStream = File.OpenRead(lexiconPath);
			var lexicon = await WordReadingLexicon.LoadAsync(lexiconStream).ConfigureAwait(false);
			strategy = new WordLexiconTokenizerStrategy(
				lexicon,
				language,
				GetReadingSystem(language));
		}
		else
		{
			var dataPath = options["--data"];
			using var dataStream = File.OpenRead(dataPath);
			var lookup = Path.GetExtension(dataPath).Equals(".zip", StringComparison.OrdinalIgnoreCase)
				? await UnihanArchiveLoader.LoadAsync(dataStream).ConfigureAwait(false)
				: await UnihanLookupLoader.LoadAsync(dataStream).ConfigureAwait(false);
			var factory = new TokenizerStrategyFactory();
			UnihanStrategyRegistration.RegisterFallbacks(
				factory,
				new UnihanCharacterReadingProvider(lookup));
			strategy = factory.GetStrategy(language);
		}

		ISpanPhoneticAligner? aligner = language.Value.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
			? new JapaneseOkuriganaAligner()
			: null;
		var document = await GlossDocumentAssembler.AssembleAsync(
			strategy.TokenizeAsync(text.AsMemory()),
			new CandidateOnlyDisambiguator(),
			aligner).ConfigureAwait(false);

		IGlossAstVisitor visitor = format.ToLowerInvariant() switch
		{
			"html" => new HtmlRubyVisitor(),
			"anki" => new AnkiSyntaxVisitor(),
			"json" => new JsonAstVisitor(),
			_ => throw new ArgumentException($"Unsupported output format '{format}'.", nameof(format))
		};

		document.Accept(visitor);
		Console.WriteLine(visitor.GetResult());
		return 0;
	}
	catch (Exception exception) when (exception is IOException
		or InvalidDataException
		or UnauthorizedAccessException
		or KeyNotFoundException
		or ArgumentException
		or FormatException)
	{
		Console.Error.WriteLine($"Error: {exception.Message}");
		return 1;
	}
}

static bool TryParseArguments(
	string[] arguments,
	out Dictionary<string, string> options,
	out string error)
{
	options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	error = string.Empty;

	for (var index = 0; index < arguments.Length; index += 2)
	{
		var name = arguments[index];
		if (name is not ("--data" or "--lexicon" or "--language" or "--text" or "--format"))
		{
			error = $"Unknown option '{name}'.";
			return false;
		}

		if (index + 1 >= arguments.Length)
		{
			error = $"Option '{name}' requires a value.";
			return false;
		}

		if (!options.TryAdd(name, arguments[index + 1]))
		{
			error = $"Option '{name}' was specified more than once.";
			return false;
		}
	}

	foreach (var required in new[] { "--language", "--text" })
	{
		if (!options.ContainsKey(required))
		{
			error = $"Required option '{required}' is missing.";
			return false;
		}
	}

	if (options.ContainsKey("--data") == options.ContainsKey("--lexicon"))
	{
		error = "Specify exactly one of '--data' or '--lexicon'.";
		return false;
	}

	return true;
}

static ReadingSystem GetReadingSystem(LanguageCode language) => language.Value.ToLowerInvariant() switch
{
	"zh-cn" => ReadingSystem.MandarinPinyin,
	"zh-hk" => ReadingSystem.CantoneseJyutping,
	"ja-jp" => ReadingSystem.JapaneseKana,
	"ar" or "ar-arab" => ReadingSystem.ArabicTashkeel,
	_ => throw new ArgumentException($"No built-in reading system is available for '{language.Value}'.", nameof(language))
};

static void PrintUsage() =>
	Console.Error.WriteLine("Usage: PhoneticAnnotator.Cli (--data <Unihan.zip|Unihan_Readings.txt> | --lexicon <word-readings.tsv>) --language <zh-CN|zh-HK|ja-JP|ar> --text <text> [--format html|anki|json]");
