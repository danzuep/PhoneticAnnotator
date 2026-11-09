using System.IO.Compression;
using System.Text;
using PhoneticAnnotator.Core;

/// <summary>Adds USFM ruby markup (<c>\rb base|reading\rb*</c>) to every USFM file in a zip.</summary>
internal static class UsfmZipAnnotator
{
	public static async Task<int> AnnotateAsync(
		string inputZip,
		string outputZip,
		ITokenizerStrategy strategy,
		ISpanPhoneticAligner? aligner)
	{
		var utf8 = new UTF8Encoding(false);
		var count = 0;
		await EnsureNoRubyAsync(inputZip, utf8).ConfigureAwait(false);
		if (File.Exists(outputZip))
		{
			File.Delete(outputZip);
		}

		using var input = ZipFile.OpenRead(inputZip);
		using var output = ZipFile.Open(outputZip, ZipArchiveMode.Create);
		foreach (var entry in input.Entries.Where(e => e.Length > 0 || !e.FullName.EndsWith('/')))
		{
			var target = output.CreateEntry(entry.FullName, CompressionLevel.Optimal);
			var isUsfm = entry.Name.EndsWith(".usfm", StringComparison.OrdinalIgnoreCase)
				|| entry.Name.EndsWith(".sfm", StringComparison.OrdinalIgnoreCase);
			await using var source = entry.Open();
			await using var destination = target.Open();
			if (!isUsfm)
			{
				await source.CopyToAsync(destination).ConfigureAwait(false);
				continue;
			}

			using var reader = new StreamReader(source, utf8);
			var lines = (await reader.ReadToEndAsync().ConfigureAwait(false)).Split('\n');
			var result = new StringBuilder();
			for (var i = 0; i < lines.Length; i++)
			{
				var line = lines[i].TrimEnd('\r');
				var document = await GlossDocumentAssembler.AssembleAsync(
					strategy.TokenizeAsync(line.AsMemory()),
					new CandidateOnlyDisambiguator(pickFirstReading: true),
					aligner).ConfigureAwait(false);
				var visitor = new UsfmRubyVisitor();
				document.Accept(visitor);
				result.Append(visitor.GetResult());
				if (i < lines.Length - 1)
				{
					result.Append('\n');
				}
			}

			var bytes = utf8.GetBytes(result.ToString());
			await destination.WriteAsync(bytes).ConfigureAwait(false);
			count++;
		}

		return count;
	}

	private static async Task EnsureNoRubyAsync(string inputZip, Encoding encoding)
	{
		using var input = ZipFile.OpenRead(inputZip);
		foreach (var entry in input.Entries.Where(e => e.Name.EndsWith(".usfm", StringComparison.OrdinalIgnoreCase)
			|| e.Name.EndsWith(".sfm", StringComparison.OrdinalIgnoreCase)))
		{
			using var reader = new StreamReader(entry.Open(), encoding);
			if ((await reader.ReadToEndAsync().ConfigureAwait(false)).Contains("\\rb ", StringComparison.Ordinal))
			{
				throw new InvalidDataException($"Ruby text already exists in '{entry.FullName}'; refusing to annotate.");
			}
		}
	}

	private sealed class UsfmRubyVisitor : IGlossAstVisitor
	{
		private readonly StringBuilder _result = new();

		public void Visit(DocumentNode node)
		{
			foreach (var child in node.Children)
			{
				child.Accept(this);
			}
		}

		public void Visit(TextNode node) => _result.Append(node.Value);

		public void Visit(RubyNode node) => Append(node.BaseText, node.GlossText);

		public void Visit(UnresolvedGlossNode node) => _result.Append(node.BaseText);

		public void Visit(PronunciationNode node) => Append(node.BaseText, node.Pronunciation);

		public string GetResult() => _result.ToString();

		private void Append(string baseText, string reading) =>
			_result.Append("\\rb ").Append(baseText).Append('|').Append(reading).Append("\\rb*");
	}
}
