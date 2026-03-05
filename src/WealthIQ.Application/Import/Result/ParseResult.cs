using WealthIQ.Application.Import.Diagnostic;

namespace WealthIQ.Application.Import.Result;

public sealed record ParseResult<T>(bool isSuccess, T? value, ImportDiagnostic? diagnostic)
{
    public static ParseResult<T> Success(T value) => new ParseResult<T>(true, value, null);
    public static ParseResult<T> Failure(ImportDiagnostic diagnostic) => new ParseResult<T>(false, default, diagnostic);

}
