using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Scripting;
using PlugLauncher.Contracts;

namespace PlugLauncher.Core;

/// <summary>
/// اسکریپت <c>.csx</c> پلاگین را با Roslyn کامپایل و اجرا می‌کند و مقدار برگشتی‌اش
/// (که باید <see cref="IPlugin"/> باشد) را تحویل می‌دهد.
///
/// خروجی کامپایل روی دیسک کش می‌شود؛ کلید کش، هش محتوای فایل‌های پلاگین است، پس تا وقتی
/// کد پلاگین دست‌نخورده بماند دفعات بعد اصلاً Roslyn لود نمی‌شود.
/// </summary>
public sealed class CsxPluginLoader(FileLogger? logger = null)
{
    private readonly FileLogger _log = logger ?? new FileLogger("loader");

    private static readonly string[] DefaultImports =
    [
        "System",
        "System.Collections.Generic",
        "System.Diagnostics",
        "System.IO",
        "System.Linq",
        "System.Text",
        "System.Threading",
        "System.Threading.Tasks",
        "PlugLauncher.Contracts"
    ];

    /// <summary>لود پلاگین؛ در صورت خطای کامپایل یا اجرا، <see cref="PluginLoadException"/> پرتاب می‌شود.</summary>
    public async Task<IPlugin> LoadAsync(PluginDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        var hash = ComputeHash(descriptor);
        var cachedAssembly = Path.Combine(
            PluginPaths.ScriptCache,
            $"{SanitizeFileName(descriptor.Id)}.{hash}.dll");

        if (File.Exists(cachedAssembly))
        {
            try
            {
                var cached = await RunCompiledAsync(cachedAssembly).ConfigureAwait(false);
                if (cached is not null)
                {
                    _log.Info($"\"{descriptor.Id}\" loaded from cache ({Path.GetFileName(cachedAssembly)})");
                    return cached;
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"cache for \"{descriptor.Id}\" was unusable, recompiling: {ex.Message}");
                TryDelete(cachedAssembly);
            }
        }

        return await CompileAndRunAsync(descriptor, cachedAssembly, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IPlugin> CompileAndRunAsync(
        PluginDescriptor descriptor,
        string outputAssembly,
        CancellationToken cancellationToken)
    {
        var code = await File.ReadAllTextAsync(descriptor.EntryFile, cancellationToken).ConfigureAwait(false);
        var options = BuildScriptOptions(descriptor);
        var script = CSharpScript.Create<object>(code, options);

        var compilation = script.GetCompilation()
            .WithOptions(script.GetCompilation().Options
                .WithOptimizationLevel(OptimizationLevel.Release)
                .WithOutputKind(OutputKind.DynamicallyLinkedLibrary));

        Directory.CreateDirectory(Path.GetDirectoryName(outputAssembly)!);

        EmitResult emitResult;
        try
        {
            emitResult = compilation.Emit(outputAssembly, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _log.Warn($"could not write compile cache for \"{descriptor.Id}\": {ex.Message}");
            return await RunInMemoryAsync(script, descriptor, cancellationToken).ConfigureAwait(false);
        }

        if (!emitResult.Success)
        {
            TryDelete(outputAssembly);
            throw new PluginLoadException(descriptor.Id, FormatDiagnostics(emitResult.Diagnostics));
        }

        try
        {
            var instance = await RunCompiledAsync(outputAssembly).ConfigureAwait(false);
            if (instance is not null) return instance;

            throw new PluginLoadException(
                descriptor.Id,
                $"The script did not return an {nameof(IPlugin)}. The last line should look like \"return Plugin.Create(...);\".");
        }
        catch (PluginLoadException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warn($"running the cached assembly for \"{descriptor.Id}\" failed, falling back to direct execution: {ex.Message}");
            TryDelete(outputAssembly);
            return await RunInMemoryAsync(script, descriptor, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>مسیر جایگزین: اجرای اسکریپت بدون کش دیسکی (وقتی emit یا reflection جواب ندهد).</summary>
    private static async Task<IPlugin> RunInMemoryAsync(
        Script<object> script,
        PluginDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var diagnostics = script.Compile(cancellationToken);
        if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            throw new PluginLoadException(descriptor.Id, FormatDiagnostics(diagnostics));

        var state = await script.RunAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return state.ReturnValue as IPlugin
               ?? throw new PluginLoadException(
                   descriptor.Id,
                   $"The script did not return an {nameof(IPlugin)}.");
    }

    /// <summary>
    /// اجرای اسمبلی‌ای که Roslyn برای اسکریپت تولید کرده. اسکریپت‌های Roslyn به کلاسی به نام
    /// <c>Submission#0</c> با متد استاتیک <c>&lt;Factory&gt;</c> کامپایل می‌شوند که آرایه‌ی
    /// submission را می‌گیرد و <c>Task&lt;object&gt;</c> برمی‌گرداند.
    /// </summary>
    private static async Task<IPlugin?> RunCompiledAsync(string assemblyPath)
    {
        var assembly = Assembly.LoadFrom(assemblyPath);
        var submission = assembly.GetType("Submission#0")
                         ?? assembly.GetTypes().FirstOrDefault(t => t.Name.StartsWith("Submission#0", StringComparison.Ordinal))
                         ?? throw new InvalidOperationException("Submission#0 was not found in the cached assembly.");

        var factory = submission.GetMethod("<Factory>", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                      ?? throw new InvalidOperationException("The <Factory> method was not found in the cached assembly.");

        // خانه‌ی صفر برای globals است (استفاده نمی‌کنیم) و خانه‌ی بعدی محل ذخیره‌ی نتیجه‌ی submission.
        var submissionArray = new object?[2];
        var result = factory.Invoke(null, [submissionArray]);

        var value = result switch
        {
            Task<object> task => await task.ConfigureAwait(false),
            Task task => await AwaitUntyped(task).ConfigureAwait(false),
            _ => result
        };

        return value as IPlugin;
    }

    private static async Task<object?> AwaitUntyped(Task task)
    {
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    private static ScriptOptions BuildScriptOptions(PluginDescriptor descriptor)
    {
        var references = new List<MetadataReference>();

        // کل framework + اسمبلی‌های خود برنامه (شامل PlugLauncher.Contracts)
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
        {
            foreach (var path in tpa.Split(Path.PathSeparator))
            {
                if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                    references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        if (references.Count == 0)
            references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

        var contracts = typeof(IPlugin).Assembly.Location;
        if (!string.IsNullOrEmpty(contracts) && references.All(r => r.Display != contracts))
            references.Add(MetadataReference.CreateFromFile(contracts));

        // اسمبلی‌های اضافه‌ی خود پلاگین (مسیر نسبی نسبت به پوشه‌ی پلاگین)
        foreach (var reference in descriptor.Manifest.References)
        {
            var path = Path.IsPathRooted(reference) ? reference : Path.Combine(descriptor.Directory, reference);
            if (File.Exists(path)) references.Add(MetadataReference.CreateFromFile(path));
        }

        return ScriptOptions.Default
            .WithFilePath(descriptor.EntryFile)
            .WithReferences(references)
            .WithImports(DefaultImports)
            .WithSourceResolver(ScriptSourceResolver.Default.WithBaseDirectory(descriptor.Directory))
            .WithMetadataResolver(ScriptMetadataResolver.Default.WithBaseDirectory(descriptor.Directory))
            .WithEmitDebugInformation(false);
    }

    /// <summary>کلید کش: هش همه‌ی فایل‌های csx پلاگین + مانیفست + نسخه‌ی اسمبلی قرارداد.</summary>
    private static string ComputeHash(PluginDescriptor descriptor)
    {
        using var sha = SHA256.Create();
        var builder = new StringBuilder();

        var files = Directory
            .EnumerateFiles(descriptor.Directory, "*.csx", SearchOption.AllDirectories)
            .Append(Path.Combine(descriptor.Directory, "plugin.json"))
            .Where(File.Exists)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            builder.Append(Path.GetFileName(file));
            builder.Append(Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(file))));
        }

        builder.Append(typeof(IPlugin).Assembly.GetName().Version);
        builder.Append(typeof(IPlugin).Assembly.ManifestModule.ModuleVersionId);

        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())))[..16];
    }

    private static string FormatDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d =>
            {
                var line = d.Location.GetLineSpan().StartLinePosition.Line + 1;
                return $"line {line}: {d.Id} {d.GetMessage()}";
            })
            .ToList();

        return errors.Count == 0 ? "Compilation failed with no specific reason." : string.Join(Environment.NewLine, errors);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // اسمبلی ممکن است لود شده و قفل باشد؛ دفعه‌ی بعد با هش جدید کنارش نوشته می‌شود
        }
    }
}

/// <summary>خطای کامپایل یا اجرای اسکریپت پلاگین.</summary>
public sealed class PluginLoadException(string pluginId, string details)
    : Exception($"Failed to load plugin \"{pluginId}\":{Environment.NewLine}{details}")
{
    public string PluginId { get; } = pluginId;
    public string Details { get; } = details;
}
