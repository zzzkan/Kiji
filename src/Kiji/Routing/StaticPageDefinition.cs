namespace Kiji.Routing;

internal sealed record StaticPageDefinition
{
    public StaticPageDefinition(
        string sourceIdentifier,
        IReadOnlyList<PageSegment> segments,
        string? routePathOverride = null,
        string? outputRelativePathOverride = null,
        bool excludeFromSitemap = false)
    {
        SourceIdentifier = sourceIdentifier;
        Segments = segments;
        RoutePathOverride = routePathOverride;
        OutputRelativePathOverride = outputRelativePathOverride;
        ExcludeFromSitemap = excludeFromSitemap;

        ValidateConfiguration();
    }

    public string SourceIdentifier { get; }

    public IReadOnlyList<PageSegment> Segments { get; }

    public string? RoutePathOverride { get; }

    public string? OutputRelativePathOverride { get; }

    public bool ExcludeFromSitemap { get; }

    public bool IsDynamic => Segments.Any(static segment => segment is PageParameterSegment);

    public IReadOnlyList<string> ParameterNames =>
        [.. Segments
            .OfType<PageParameterSegment>()
            .Select(static segment => segment.Name)];

    public static StaticPageDefinition Create(
        string routeTemplate,
        string? routePathOverride = null,
        string? outputRelativePathOverride = null,
        bool excludeFromSitemap = false)
    {
        var normalizedTemplate = NormalizeRouteTemplate(routeTemplate);

        return new StaticPageDefinition(
            normalizedTemplate,
            ParseSegments(normalizedTemplate),
            routePathOverride,
            outputRelativePathOverride,
            excludeFromSitemap);
    }

    public static string NormalizeRouteTemplate(string routeTemplate)
    {
        if (string.IsNullOrWhiteSpace(routeTemplate))
        {
            throw new InvalidOperationException("Route template cannot be empty.");
        }

        var normalized = routeTemplate.Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = '/' + normalized;
        }

        if (normalized.Length > 1 && !normalized.EndsWith('/'))
        {
            normalized += '/';
        }

        return normalized;
    }

    public BoundPagePath BindPath(
        IReadOnlyDictionary<string, string>? parameters = null,
        Func<string, string>? parameterValueTransformer = null)
    {
        var transform = parameterValueTransformer ??= static value => value;
        var boundSegments = ResolveBoundSegments(parameters, transform);

        return new BoundPagePath(
            BuildRoutePath(boundSegments),
            BuildOutputRelativePath(boundSegments));
    }

    public string ResolveRoutePath(IReadOnlyDictionary<string, string>? parameters = null)
    {
        return BindPath(parameters, static value => Uri.EscapeDataString(value)).RoutePath;
    }

    public string ResolveOutputRelativePath(IReadOnlyDictionary<string, string>? parameters = null)
    {
        return BindPath(parameters).OutputRelativePath;
    }

    public BoundPagePath ResolveStaticPath()
    {
        return RoutePathOverride is not null && OutputRelativePathOverride is not null
            ? new BoundPagePath(RoutePathOverride, OutputRelativePathOverride)
            : BindPath();
    }

    private static IReadOnlyList<PageSegment> ParseSegments(string routeTemplate)
    {
        var normalizedTemplate = NormalizeRouteTemplate(routeTemplate);
        if (string.Equals(normalizedTemplate, "/", StringComparison.Ordinal))
        {
            return [];
        }

        return [.. normalizedTemplate
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => ParseSegment(routeTemplate, segment))];
    }

    private static PageSegment ParseSegment(string routeTemplate, string segment)
    {
        if (segment.Length > 2 && segment[0] == '{' && segment[^1] == '}')
        {
            var token = segment[1..^1];
            if (token.StartsWith('*'))
            {
                throw CreateUnsupportedRouteTemplateException(routeTemplate, segment, "catch-all parameters");
            }

            if (token.Contains(':', StringComparison.Ordinal))
            {
                throw CreateUnsupportedRouteTemplateException(routeTemplate, segment, "route constraints");
            }

            if (token.EndsWith('?'))
            {
                throw CreateUnsupportedRouteTemplateException(routeTemplate, segment, "optional parameters");
            }

            if (!IsSupportedParameterName(token))
            {
                throw CreateUnsupportedRouteTemplateException(routeTemplate, segment, "parameter syntax");
            }

            return new PageParameterSegment(token);
        }

        if (segment.Contains('{', StringComparison.Ordinal) || segment.Contains('}', StringComparison.Ordinal))
        {
            throw CreateUnsupportedRouteTemplateException(routeTemplate, segment, "composite segment syntax");
        }

        return new PageLiteralSegment(segment);
    }

    private static bool IsSupportedParameterName(string parameterName)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
        {
            return false;
        }

        if (!IsSupportedParameterNameStart(parameterName[0]))
        {
            return false;
        }

        return parameterName[1..].All(IsSupportedParameterNamePart);
    }

    private static bool IsSupportedParameterNameStart(char value)
    {
        return char.IsLetter(value) || value == '_';
    }

    private static bool IsSupportedParameterNamePart(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_';
    }

    private static InvalidOperationException CreateUnsupportedRouteTemplateException(
        string routeTemplate,
        string segment,
        string reason)
    {
        return new InvalidOperationException(
            $"Route template '{routeTemplate}' uses unsupported {reason} in segment '{segment}'. Static generation supports only literal segments and simple route parameters like '{{Slug}}'.");
    }

    private string[] ResolveBoundSegments(
        IReadOnlyDictionary<string, string>? parameters,
        Func<string, string> parameterValueTransformer)
    {
        if (Segments.Count == 0)
        {
            return [];
        }

        return [.. Segments.Select(segment => segment switch
        {
            PageLiteralSegment literal => literal.Value,
            PageParameterSegment parameter => parameterValueTransformer(GetParameterValue(parameter.Name, parameters)),
            _ => throw new InvalidOperationException($"Unknown page segment type '{segment.GetType().Name}'."),
        })];
    }

    private void ValidateConfiguration()
    {
        var hasPathOverrides = RoutePathOverride is not null || OutputRelativePathOverride is not null;
        if (RoutePathOverride is null != OutputRelativePathOverride is null)
        {
            throw new InvalidOperationException(
                $"Route template '{SourceIdentifier}' must specify route and output overrides together.");
        }

        if (IsDynamic && hasPathOverrides)
        {
            throw new InvalidOperationException(
                $"Dynamic route template '{SourceIdentifier}' cannot override its resolved route or output path.");
        }
    }

    private static string BuildRoutePath(string[] boundSegments)
    {
        if (boundSegments.Length == 0)
        {
            return "/";
        }

        return '/' + string.Join('/', boundSegments) + '/';
    }

    private static string BuildOutputRelativePath(string[] boundSegments)
    {
        if (boundSegments.Length == 0)
        {
            return "index.html";
        }

        return Path.Combine([.. boundSegments, "index.html"]);
    }

    private static string GetParameterValue(string name, IReadOnlyDictionary<string, string>? parameters)
    {
        if (parameters is null || !parameters.TryGetValue(name, out var value))
        {
            throw new InvalidOperationException($"Route value '{name}' was not provided.");
        }

        return value;
    }

    internal abstract record PageSegment;

    internal sealed record PageLiteralSegment(string Value) : PageSegment;

    internal sealed record PageParameterSegment(string Name) : PageSegment;

    internal sealed record BoundPagePath(string RoutePath, string OutputRelativePath);
}
