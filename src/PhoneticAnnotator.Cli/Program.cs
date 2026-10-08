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
		var dataPath = options["--data"];
		var language = new LanguageCode(options["--language"]);
		var text = options["--text"];
		var format = options.GetValueOrDefault("--format", "html");

		using var dataStream = File.OpenRead(dataPath);
		var lookup = Path.GetExtension(dataPath).Equals(".zip", StringComparison.OrdinalIgnoreCase)
			? await UnihanArchiveLoader.LoadAsync(dataStream).ConfigureAwait(false)
			: await UnihanLookupLoader.LoadAsync(dataStream).ConfigureAwait(false);
		var factory = new TokenizerStrategyFactory();
		UnihanStrategyRegistration.RegisterFallbacks(
			factory,
			new UnihanCharacterReadingProvider(lookup));

		var strategy = factory.GetStrategy(language);
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
		or ArgumentException)
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
		if (name is not ("--data" or "--language" or "--text" or "--format"))
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

	foreach (var required in new[] { "--data", "--language", "--text" })
	{
		if (!options.ContainsKey(required))
		{
			error = $"Required option '{required}' is missing.";
			return false;
		}
	}

	return true;
}

static void PrintUsage() =>
	Console.Error.WriteLine("Usage: PhoneticAnnotator.Cli --data <Unihan.zip|Unihan_Readings.txt> --language <zh-CN|zh-HK|ja-JP> --text <text> [--format html|anki|json]");
