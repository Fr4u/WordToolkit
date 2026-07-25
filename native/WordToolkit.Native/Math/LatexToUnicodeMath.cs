using System.Text;
using WordToolkit.Native.Protocol;

namespace WordToolkit.Native.Equations;

internal static class LatexToUnicodeMath
{
    private const char NaryBodySeparator = '▒';
    private const char MatrixMarker = '■';
    private const char ParenthesizedMatrixMarker = '⒨';
    private const char BracketedMatrixMarker = 'ⓢ';
    private const char DeterminantMatrixMarker = '⒱';
    private const char NormMatrixMarker = '⒩';
    private const char EquationArrayMarker = '█';
    private const char CasesMarker = 'Ⓒ';
    private const char BelowMarker = '┬';
    private const char CaseColumnSpace = WordMathSpacing.CaseColumn;
    private const char TextBoundarySpace = WordMathSpacing.TextBoundary;

    private static readonly HashSet<char> NaryOperators =
    [
        '∑',
        '∏',
        '∐',
        '∫',
        '∬',
        '∭',
        '∮',
        '⋃',
        '⋂',
    ];

    private static readonly IReadOnlyDictionary<string, string> Symbols =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["alpha"] = "α",
            ["beta"] = "β",
            ["gamma"] = "γ",
            ["delta"] = "δ",
            ["epsilon"] = "ε",
            ["varepsilon"] = "ϵ",
            ["zeta"] = "ζ",
            ["eta"] = "η",
            ["theta"] = "θ",
            ["vartheta"] = "ϑ",
            ["iota"] = "ι",
            ["kappa"] = "κ",
            ["lambda"] = "λ",
            ["mu"] = "μ",
            ["nu"] = "ν",
            ["xi"] = "ξ",
            ["omicron"] = "ο",
            ["pi"] = "π",
            ["varpi"] = "ϖ",
            ["rho"] = "ρ",
            ["varrho"] = "ϱ",
            ["sigma"] = "σ",
            ["varsigma"] = "ς",
            ["tau"] = "τ",
            ["upsilon"] = "υ",
            ["phi"] = "φ",
            ["varphi"] = "ϕ",
            ["chi"] = "χ",
            ["psi"] = "ψ",
            ["omega"] = "ω",
            ["Gamma"] = "Γ",
            ["Delta"] = "Δ",
            ["Theta"] = "Θ",
            ["Lambda"] = "Λ",
            ["Xi"] = "Ξ",
            ["Pi"] = "Π",
            ["Sigma"] = "Σ",
            ["Upsilon"] = "Υ",
            ["Phi"] = "Φ",
            ["Psi"] = "Ψ",
            ["Omega"] = "Ω",
            ["sum"] = "∑",
            ["prod"] = "∏",
            ["coprod"] = "∐",
            ["int"] = "∫",
            ["iint"] = "∬",
            ["iiint"] = "∭",
            ["oint"] = "∮",
            ["bigcup"] = "⋃",
            ["bigcap"] = "⋂",
            ["infty"] = "∞",
            ["partial"] = "∂",
            ["nabla"] = "∇",
            ["hbar"] = "ℏ",
            ["ell"] = "ℓ",
            ["Re"] = "ℜ",
            ["Im"] = "ℑ",
            ["aleph"] = "ℵ",
            ["forall"] = "∀",
            ["exists"] = "∃",
            ["nexists"] = "∄",
            ["in"] = "∈",
            ["notin"] = "∉",
            ["ni"] = "∋",
            ["emptyset"] = "∅",
            ["varnothing"] = "∅",
            ["cup"] = "∪",
            ["cap"] = "∩",
            ["subset"] = "⊂",
            ["supset"] = "⊃",
            ["subseteq"] = "⊆",
            ["supseteq"] = "⊇",
            ["land"] = "∧",
            ["lor"] = "∨",
            ["neg"] = "¬",
            ["le"] = "≤",
            ["leq"] = "≤",
            ["ge"] = "≥",
            ["geq"] = "≥",
            ["ne"] = "≠",
            ["neq"] = "≠",
            ["approx"] = "≈",
            ["sim"] = "∼",
            ["simeq"] = "≃",
            ["equiv"] = "≡",
            ["propto"] = "∝",
            ["to"] = "→",
            ["rightarrow"] = "→",
            ["leftarrow"] = "←",
            ["leftrightarrow"] = "↔",
            ["Rightarrow"] = "⇒",
            ["Leftarrow"] = "⇐",
            ["Leftrightarrow"] = "⇔",
            ["mapsto"] = "↦",
            ["times"] = "×",
            ["cdot"] = "·",
            ["bullet"] = "∙",
            ["div"] = "÷",
            ["pm"] = "±",
            ["mp"] = "∓",
            ["circ"] = "∘",
            ["degree"] = "°",
            ["dagger"] = "†",
            ["ddagger"] = "‡",
            ["dd"] = WordLinearMathNormalizer.DifferentialD.ToString(),
            ["dots"] = "…",
            ["ldots"] = "…",
            ["cdots"] = "⋯",
            ["vdots"] = "⋮",
            ["ddots"] = "⋱",
            ["angle"] = "∠",
            ["perp"] = "⊥",
            ["parallel"] = "∥",
        };

    private static readonly HashSet<string> Functions =
    [
        "sin",
        "cos",
        "tan",
        "cot",
        "sec",
        "csc",
        "arcsin",
        "arccos",
        "arctan",
        "sinh",
        "cosh",
        "tanh",
        "log",
        "ln",
        "exp",
        "min",
        "max",
        "lim",
        "det",
        "gcd",
    ];

    private static readonly HashSet<string> QuotedOperators =
    [
        "min",
        "max",
        "det",
        "gcd",
    ];

    private static readonly IReadOnlyDictionary<string, string> Delimiters =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["langle"] = "⟨",
            ["rangle"] = "⟩",
            ["lvert"] = "|",
            ["rvert"] = "|",
            ["vert"] = "|",
            ["lVert"] = "‖",
            ["rVert"] = "‖",
            ["Vert"] = "‖",
            ["lbrace"] = "{",
            ["rbrace"] = "}",
            ["lfloor"] = "⌊",
            ["rfloor"] = "⌋",
            ["lceil"] = "⌈",
            ["rceil"] = "⌉",
        };

    public static string Convert(string source)
    {
        return ConvertPlan(source).Linear;
    }

    internal static EquationConversionPlan ConvertPlan(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw Invalid("LaTeX equation input is empty");
        }
        if (source.Length > 100_000)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "LaTeX equation exceeds 100,000 characters"
            );
        }
        EquationFormattingMarkers.RejectReservedInput(source, "latex");
        var parser = new Parser(StripMathDelimiters(source));
        var converted = parser.ParseAll();
        var plan = EquationFormattingMarkers.FromMarkedLinear(converted);
        if (plan.Linear.Length == 0)
        {
            throw Invalid("LaTeX equation produced no Word math");
        }
        if (plan.BuildLinear.Length > 200_000)
        {
            throw new NativeToolException(
                "LIMIT_EXCEEDED",
                "Converted Word equation exceeds 200,000 characters"
            );
        }
        return plan;
    }

    private static string StripMathDelimiters(string source)
    {
        var value = source.Trim();
        if (value.Length >= 4 && value.StartsWith("$$") && value.EndsWith("$$"))
        {
            return value[2..^2].Trim();
        }
        if (value.Length >= 2 && value.StartsWith('$') && value.EndsWith('$'))
        {
            return value[1..^1].Trim();
        }
        if (
            value.Length >= 4
            && (
                (value.StartsWith(@"\(") && value.EndsWith(@"\)"))
                || (value.StartsWith(@"\[") && value.EndsWith(@"\]"))
            )
        )
        {
            return value[2..^2].Trim();
        }
        return value;
    }

    private sealed class Parser
    {
        private const int MaximumDepth = 128;
        private readonly string _source;
        private int _index;
        private int _depth;
        private bool _nextPlainDIsDifferential;

        public Parser(string source)
        {
            _source = source;
        }

        public string ParseAll()
        {
            var value = ParseSequence(null).Trim();
            if (_index != _source.Length)
            {
                throw Invalid("Unexpected trailing LaTeX input", new { index = _index });
            }
            return WordLinearMathNormalizer.NormalizeForWord(
                NormalizeSpaces(value)
            );
        }

        private string ParseSequence(char? terminator)
        {
            Enter();
            try
            {
                var output = new StringBuilder();
                while (_index < _source.Length)
                {
                    var character = _source[_index];
                    if (terminator is not null && character == terminator)
                    {
                        return output.ToString();
                    }
                    if (character == '}')
                    {
                        throw Invalid("Unmatched closing LaTeX brace", new { index = _index });
                    }
                    if (character == '%')
                    {
                        throw Invalid(
                            "LaTeX comments are not accepted in live equations",
                            new { index = _index }
                        );
                    }
                    if (character is CaseColumnSpace or TextBoundarySpace)
                    {
                        _index++;
                        output.Append(character);
                        continue;
                    }
                    if (char.IsWhiteSpace(character))
                    {
                        SkipWhitespace();
                        AppendSpace(output);
                        continue;
                    }
                    if (character is '^' or '_')
                    {
                        throw Invalid(
                            "A LaTeX script marker has no base",
                            new { index = _index, marker = character.ToString() }
                        );
                    }
                    var baseAtom = ParseAtom();
                    if (baseAtom.Length == 0)
                    {
                        continue;
                    }
                    var atom = ParseScripts(baseAtom);
                    if (
                        baseAtom is "lim" or "\"min\"" or "\"max\""
                        && atom.Length > baseAtom.Length
                        && atom[baseAtom.Length] == '_'
                    )
                    {
                        atom =
                            baseAtom
                            + BelowMarker
                            + atom[(baseAtom.Length + 1)..];
                        var argument = ParseFunctionArgument();
                        atom += "\u2061";
                        atom += HasTopLevelDivision(argument)
                            ? $"〖{argument}〗"
                            : argument;
                    }
                    else if (
                        Functions.Contains(baseAtom)
                        && !QuotedOperators.Contains(baseAtom)
                        && atom.StartsWith(baseAtom + "^(", StringComparison.Ordinal)
                    )
                    {
                        var argument = ParseFunctionArgument();
                        atom = $"({baseAtom} {argument}){atom[baseAtom.Length..]}";
                    }
                    if (
                        IsNaryAtom(atom)
                        && !NextNonWhitespaceIsNaryBodySeparator()
                    )
                    {
                        atom += NaryBodySeparator;
                    }
                    AppendAtom(output, atom);
                }
                if (terminator is not null)
                {
                    throw Invalid(
                        "Unclosed LaTeX group",
                        new { expected = terminator.ToString() }
                    );
                }
                return output.ToString();
            }
            finally
            {
                _depth--;
            }
        }

        private string ParseAtom()
        {
            var character = _source[_index++];
            var isDifferential = _nextPlainDIsDifferential && character == 'd';
            _nextPlainDIsDifferential = false;
            if (isDifferential)
            {
                return WordLinearMathNormalizer.DifferentialD.ToString();
            }
            if (character == '{')
            {
                var body = ParseSequence('}');
                Expect('}');
                return $"({body.Trim()})";
            }
            if (character == '\\')
            {
                return ParseCommand();
            }
            if (character == '&')
            {
                throw Invalid(
                    "A LaTeX alignment marker is only valid inside a supported environment",
                    new { index = _index - 1 }
                );
            }
            return character.ToString();
        }

        private string ParseScripts(string atom)
        {
            while (true)
            {
                var beforeWhitespace = _index;
                SkipWhitespace();
                if (TryConsumeCommand("limits"))
                {
                    if (!IsNaryAtom(atom))
                    {
                        throw Invalid("\\limits must follow an n-ary operator");
                    }
                    continue;
                }
                if (TryConsumeCommand("nolimits"))
                {
                    throw Invalid(
                        "\\nolimits cannot be preserved by Word's linear OMath build-up"
                    );
                }
                if (_index >= _source.Length || _source[_index] is not ('^' or '_'))
                {
                    _index = beforeWhitespace;
                    return atom;
                }
                var marker = _source[_index++];
                SkipWhitespace();
                var value = ParseScriptArgument();
                atom += $"{marker}({value.Trim()})";
            }
        }

        private bool TryConsumeCommand(string command)
        {
            var token = $@"\{command}";
            if (
                !_source.AsSpan(_index).StartsWith(token, StringComparison.Ordinal)
                || _index + token.Length < _source.Length
                    && char.IsLetter(_source[_index + token.Length])
            )
            {
                return false;
            }
            _index += token.Length;
            return true;
        }

        private string ParseScriptArgument()
        {
            if (_index >= _source.Length)
            {
                throw Invalid("A LaTeX script has no value");
            }
            if (_source[_index] == '{')
            {
                _index++;
                var body = ParseSequence('}');
                Expect('}');
                return body;
            }
            return ParseAtom();
        }

        private string ParseFunctionArgument()
        {
            SkipWhitespace();
            if (_index >= _source.Length || _source[_index] == '}')
            {
                throw Invalid("A limit operator has no following argument");
            }
            string basis;
            if (_source[_index] == '(')
            {
                _index++;
                var body = ParseSequence(')');
                Expect(')');
                basis = $"({body.Trim()})";
            }
            else
            {
                basis = ParseAtom();
            }
            if (basis.Length == 0)
            {
                throw Invalid("A limit operator has an empty following argument");
            }
            var argument = ParseScripts(basis);
            if (
                IsNaryAtom(argument)
                && !NextNonWhitespaceIsNaryBodySeparator()
            )
            {
                argument += NaryBodySeparator;
            }
            return argument;
        }

        private string ParseCommand()
        {
            if (_index >= _source.Length)
            {
                throw Invalid("LaTeX input ends with an incomplete command");
            }
            if (_source[_index] == '\\')
            {
                throw Invalid(
                    "A LaTeX row separator is only valid inside a supported environment",
                    new { index = _index - 1 }
                );
            }
            if (!char.IsLetter(_source[_index]))
            {
                var escaped = _source[_index++];
                if (escaped is ',' or ';' or ':' or ' ' or '!')
                {
                    if (NextSourceAtomIsPlainDifferential())
                    {
                        _nextPlainDIsDifferential = true;
                    }
                    return escaped == '!' ? "" : " ";
                }
                return escaped switch
                {
                    '{' => "{",
                    '}' => "}",
                    '|' => "‖",
                    '_' => "_",
                    '%' => "%",
                    '#' => "#",
                    '$' => "$",
                    '&' => "&",
                    _ => throw Invalid(
                        "Unsupported escaped LaTeX character",
                        new { character = escaped.ToString(), index = _index - 1 }
                    ),
                };
            }
            var start = _index;
            while (_index < _source.Length && char.IsLetter(_source[_index]))
            {
                _index++;
            }
            var command = _source[start.._index];
            if (_index < _source.Length && _source[_index] == '*')
            {
                _index++;
            }

            if (command is "frac" or "dfrac" or "tfrac")
            {
                var numerator = ParseRequiredGroup(command);
                var denominator = ParseRequiredGroup(command);
                return $"({numerator.Trim()})/({denominator.Trim()})";
            }
            if (command == "binom")
            {
                var upper = ParseRequiredGroup(command);
                var lower = ParseRequiredGroup(command);
                return $"({upper.Trim()}¦{lower.Trim()})";
            }
            if (command == "sqrt")
            {
                SkipWhitespace();
                string? degree = null;
                if (_index < _source.Length && _source[_index] == '[')
                {
                    _index++;
                    degree = ParseBracketGroup();
                }
                var body = ParseRequiredGroup(command);
                return degree is null
                    ? $"√({body.Trim()})"
                    : $"√({degree.Trim()}&{body.Trim()})";
            }
            if (command == "text")
            {
                var rawText = ReadRequiredRawGroup(command);
                var hasLeadingSpace = rawText.Length > 0
                    && char.IsWhiteSpace(rawText[0]);
                var hasTrailingSpace = rawText.Length > 0
                    && char.IsWhiteSpace(rawText[^1]);
                var trimmedText = rawText.Trim();
                if (trimmedText.Length == 0)
                {
                    return rawText.Length == 0
                        ? "\"\""
                        : TextBoundarySpace.ToString();
                }
                var text = trimmedText
                    .Replace("\\}", "}", StringComparison.Ordinal)
                    .Replace("\\{", "{", StringComparison.Ordinal)
                    .Replace("\\\\", "\\", StringComparison.Ordinal)
                    .Replace("\"", "\"\"", StringComparison.Ordinal);
                return string.Concat(
                    hasLeadingSpace ? TextBoundarySpace.ToString() : string.Empty,
                    "\"",
                    text,
                    "\"",
                    hasTrailingSpace ? TextBoundarySpace.ToString() : string.Empty
                );
            }
            if (command == "operatorname")
            {
                var name = ReadRequiredRawGroup(command).Trim();
                if (
                    name.Length == 0
                    || name.Any(character => !char.IsLetterOrDigit(character))
                )
                {
                    throw Invalid(
                        "\\operatorname requires a non-empty alphanumeric name",
                        new { name }
                    );
                }
                return name == "d"
                    ? WordLinearMathNormalizer.DifferentialD.ToString()
                    : $"\"{name}\"";
            }
            if (
                command
                    is "mathrm"
                    or "mathit"
                    or "mathsf"
                    or "mathtt"
                    or "mathcal"
                    or "mathbb"
                    or "mathfrak"
            )
            {
                var body = ReadRequiredRawGroup(command);
                if (body.IndexOfAny(['\\', '{', '}']) >= 0)
                {
                    throw Invalid(
                        $"\\{command} does not accept nested LaTeX commands in this converter"
                    );
                }
                if (command == "mathrm")
                {
                    return body.Trim() == "d"
                        ? WordLinearMathNormalizer.DifferentialD.ToString()
                        : RomanText(body, command);
                }
                return command switch
                {
                    "mathcal" => MathAlphabetMapper.Apply(
                        body,
                        MathAlphabetStyle.Script
                    ),
                    "mathbb" => MathAlphabetMapper.Apply(
                        body,
                        MathAlphabetStyle.DoubleStruck
                    ),
                    "mathfrak" => MathAlphabetMapper.Apply(
                        body,
                        MathAlphabetStyle.Fraktur
                    ),
                    "mathsf" => MathAlphabetMapper.Apply(
                        body,
                        MathAlphabetStyle.SansSerif
                    ),
                    "mathtt" => MathAlphabetMapper.Apply(
                        body,
                        MathAlphabetStyle.Monospace
                    ),
                    _ => body,
                };
            }
            if (command is "mathbf" or "boldsymbol")
            {
                var body = ParseRequiredGroup(command);
                return EquationFormattingMarkers.Wrap(
                    command == "mathbf"
                        ? EquationMathStyle.Bold
                        : EquationMathStyle.BoldItalic,
                    body
                );
            }
            if (command is "displaystyle" or "textstyle")
            {
                return ParseRequiredGroup(command);
            }
            if (
                command
                    is "vec"
                    or "hat"
                    or "bar"
                    or "overline"
                    or "tilde"
                    or "dot"
                    or "ddot"
            )
            {
                var body = ParseRequiredGroup(command);
                var accent = command == "overline" ? "bar" : command;
                return ApplyAccent(body.Trim(), accent);
            }
            if (command is "left" or "right")
            {
                SkipWhitespace();
                return ParseDelimiter(command);
            }
            if (command == "begin")
            {
                var environment = ReadRequiredRawGroup(command);
                return ParseEnvironment(environment);
            }
            if (command == "end")
            {
                throw Invalid("Unexpected LaTeX environment terminator");
            }
            if (
                command
                    is "quad"
                    or "qquad"
                    or "enspace"
                    or "thinspace"
                    or "medspace"
                    or "thickspace"
            )
            {
                return " ";
            }
            if (command == "limits")
            {
                throw Invalid("\\limits must follow an n-ary operator");
            }
            if (command == "nolimits")
            {
                throw Invalid(
                    "\\nolimits cannot be preserved by Word's linear OMath build-up"
                );
            }
            if (Symbols.TryGetValue(command, out var symbol))
            {
                return symbol;
            }
            if (Functions.Contains(command))
            {
                return QuotedOperators.Contains(command)
                    ? $"\"{command}\""
                    : command;
            }
            if (Delimiters.TryGetValue(command, out var delimiter))
            {
                return delimiter;
            }
            throw Invalid(
                "Unsupported LaTeX command for native Word conversion",
                new { command = $@"\{command}" }
            );
        }

        private string ParseDelimiter(string command)
        {
            if (_index >= _source.Length)
            {
                throw Invalid($"\\{command} has no delimiter");
            }
            if (_source[_index] == '.')
            {
                _index++;
                return "";
            }
            if (_source[_index] != '\\')
            {
                return _source[_index++].ToString();
            }
            _index++;
            if (_index >= _source.Length)
            {
                throw Invalid($"\\{command} has an incomplete escaped delimiter");
            }
            if (!char.IsLetter(_source[_index]))
            {
                var escaped = _source[_index++];
                return escaped switch
                {
                    '|' => "‖",
                    '{' => "{",
                    '}' => "}",
                    _ => throw Invalid(
                        "Unsupported LaTeX delimiter",
                        new { command, delimiter = $@"\{escaped}" }
                    ),
                };
            }
            var start = _index;
            while (_index < _source.Length && char.IsLetter(_source[_index]))
            {
                _index++;
            }
            var name = _source[start.._index];
            if (Delimiters.TryGetValue(name, out var value))
            {
                return value;
            }
            throw Invalid(
                "Unsupported LaTeX delimiter",
                new { command, delimiter = $@"\{name}" }
            );
        }

        private static string RomanText(string body, string command)
        {
            var text = body.Trim();
            if (
                text.Length == 0
                || text.Any(character =>
                    !char.IsLetterOrDigit(character) && !char.IsWhiteSpace(character)
                )
            )
            {
                throw Invalid(
                    $"\\{command} currently requires plain alphanumeric text"
                );
            }
            return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        private string ParseEnvironment(string environment)
        {
            var supported = environment.TrimEnd('*');
            if (
                supported
                    is not (
                        "matrix"
                        or "pmatrix"
                        or "bmatrix"
                        or "vmatrix"
                        or "Vmatrix"
                        or "cases"
                        or "aligned"
                        or "align"
                        or "gathered"
                    )
            )
            {
                throw Invalid(
                    "Unsupported LaTeX environment for native Word conversion",
                    new { environment }
                );
            }
            var terminator = $@"\end{{{environment}}}";
            var end = _source.IndexOf(terminator, _index, StringComparison.Ordinal);
            if (end < 0)
            {
                throw Invalid(
                    "LaTeX environment has no matching end",
                    new { environment }
                );
            }
            var body = _source[_index..end];
            _index = end + terminator.Length;
            var rows = SplitTopLevel(body, rowSeparator: true);
            if (rows.Count == 0)
            {
                throw Invalid("LaTeX equation environment is empty", new { environment });
            }
            if (supported is "aligned" or "align" or "gathered" or "cases")
            {
                var convertedRows = rows
                    .Select(
                        row =>
                            new Parser(
                                string.Join(
                                    supported == "cases"
                                        ? CaseColumnSpace.ToString()
                                        : " ",
                                    SplitTopLevel(row, rowSeparator: false)
                                )
                            ).ParseAll()
                    )
                    .ToArray();
                var name = supported == "cases"
                    ? CasesMarker.ToString()
                    : EquationArrayMarker.ToString();
                return $"{name}({string.Join("@", convertedRows)})";
            }
            var convertedMatrixRows = rows
                .Select(
                    row =>
                        string.Join(
                            "&",
                            SplitTopLevel(row, rowSeparator: false)
                                .Select(cell => new Parser(cell).ParseAll())
                        )
                )
                .ToArray();
            var matrixBody = string.Join("@", convertedMatrixRows);
            return supported switch
            {
                "pmatrix" => $"{ParenthesizedMatrixMarker}({matrixBody})",
                "bmatrix" => $"{BracketedMatrixMarker}({matrixBody})",
                "vmatrix" => $"{DeterminantMatrixMarker}({matrixBody})",
                "Vmatrix" => $"{NormMatrixMarker}({matrixBody})",
                _ => $"{MatrixMarker}({matrixBody})",
            };
        }

        private string ParseRequiredGroup(string command)
        {
            SkipWhitespace();
            if (_index >= _source.Length || _source[_index] != '{')
            {
                throw Invalid(
                    $"\\{command} requires a braced argument",
                    new { index = _index }
                );
            }
            _index++;
            var value = ParseSequence('}');
            Expect('}');
            return value;
        }

        private string ReadRequiredRawGroup(string command)
        {
            SkipWhitespace();
            if (_index >= _source.Length || _source[_index] != '{')
            {
                throw Invalid(
                    $"\\{command} requires a braced argument",
                    new { index = _index }
                );
            }
            _index++;
            var start = _index;
            var depth = 1;
            var escaped = false;
            while (_index < _source.Length)
            {
                var character = _source[_index++];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (character == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (character == '{')
                {
                    depth++;
                }
                else if (character == '}' && --depth == 0)
                {
                    return _source[start..(_index - 1)];
                }
            }
            throw Invalid($"\\{command} has an unclosed braced argument");
        }

        private string ParseBracketGroup()
        {
            var start = _index;
            var depth = 1;
            while (_index < _source.Length)
            {
                var character = _source[_index++];
                if (character == '[')
                {
                    depth++;
                }
                else if (character == ']' && --depth == 0)
                {
                    var source = _source[start..(_index - 1)];
                    return new Parser(source).ParseAll();
                }
            }
            throw Invalid("LaTeX radical degree has no closing bracket");
        }

        private void SkipWhitespace()
        {
            while (_index < _source.Length && char.IsWhiteSpace(_source[_index]))
            {
                _index++;
            }
        }

        private bool NextNonWhitespaceIsNaryBodySeparator()
        {
            var index = _index;
            while (index < _source.Length && char.IsWhiteSpace(_source[index]))
            {
                index++;
            }
            return index < _source.Length && _source[index] == NaryBodySeparator;
        }

        private bool NextSourceAtomIsPlainDifferential()
        {
            var index = _index;
            while (index < _source.Length && char.IsWhiteSpace(_source[index]))
            {
                index++;
            }
            if (index >= _source.Length || _source[index] != 'd')
            {
                return false;
            }
            index++;
            if (index >= _source.Length)
            {
                return false;
            }
            return char.IsWhiteSpace(_source[index])
                || char.IsLetterOrDigit(_source[index])
                || _source[index] is '\\' or '{' or '(';
        }

        private void Expect(char expected)
        {
            if (_index >= _source.Length || _source[_index] != expected)
            {
                throw Invalid(
                    "Unexpected LaTeX token",
                    new
                    {
                        index = _index,
                        expected = expected.ToString(),
                        actual = _index < _source.Length
                            ? _source[_index].ToString()
                            : "<end>",
                    }
                );
            }
            _index++;
        }

        private void Enter()
        {
            _depth++;
            if (_depth > MaximumDepth)
            {
                throw new NativeToolException(
                    "LIMIT_EXCEEDED",
                    "LaTeX nesting exceeds 128 levels"
                );
            }
        }
    }

    private static List<string> SplitTopLevel(string source, bool rowSeparator)
    {
        var parts = new List<string>();
        var start = 0;
        var depth = 0;
        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (character == '{' && (index == 0 || source[index - 1] != '\\'))
            {
                depth++;
            }
            else if (
                character == '}'
                && (index == 0 || source[index - 1] != '\\')
            )
            {
                depth--;
                if (depth < 0)
                {
                    throw Invalid("Unbalanced braces inside LaTeX environment");
                }
            }
            else if (
                depth == 0
                && (
                    (rowSeparator
                        && character == '\\'
                        && index + 1 < source.Length
                        && source[index + 1] == '\\')
                    || (!rowSeparator && character == '&')
                )
            )
            {
                parts.Add(source[start..index].Trim());
                index += rowSeparator ? 1 : 0;
                start = index + 1;
            }
        }
        if (depth != 0)
        {
            throw Invalid("Unbalanced braces inside LaTeX environment");
        }
        parts.Add(source[start..].Trim());
        return parts.Where(part => part.Length > 0).ToList();
    }

    private static void AppendSpace(StringBuilder output)
    {
        if (
            output.Length > 0
            && output[^1] is not (' ' or CaseColumnSpace or TextBoundarySpace)
        )
        {
            output.Append(' ');
        }
    }

    private static void AppendAtom(StringBuilder output, string atom)
    {
        if (
            output.Length > 0
            && output[^1] == ' '
            && atom.Length > 0
            && (
                atom[0] == '"'
                || (
                    atom[0] == TextBoundarySpace
                    && atom.Length > 1
                    && atom[1] == '"'
                )
            )
        )
        {
            output[^1] = TextBoundarySpace;
        }
        if (
            output.Length > 0
            && output[^1] != ' '
            && (
                StartsWithIdentifier(atom)
                    && EndsWithIdentifierOrStructuredFactor(output)
                || StartsWithOpeningDelimiter(atom)
                    && EndsWithStructuredFactor(output)
            )
            && output[^1] != WordLinearMathNormalizer.DifferentialD
        )
        {
            output.Append(' ');
        }
        output.Append(atom);
    }

    private static string ApplyAccent(string body, string accent)
    {
        var mark = accent switch
        {
            "vec" => "\u20D7",
            "hat" => "\u0302",
            "bar" => "\u0305",
            "tilde" => "\u0303",
            "dot" => "\u0307",
            "ddot" => "\u0308",
            _ => throw Invalid("Unsupported Word accent", new { accent }),
        };
        return $"{ParenthesizeBase(body)}{mark}";
    }

    private static string ParenthesizeBase(string value)
    {
        return value.Length == 1 ? value : $"({value})";
    }

    private static bool StartsWithIdentifier(string value)
    {
        return value.Length > 0 && char.IsLetter(value[0]);
    }

    private static bool StartsWithOpeningDelimiter(string value) =>
        value.Length > 0 && value[0] is '(' or '[' or '{' or '〖';

    private static bool IsNaryAtom(string value)
    {
        return value.Length > 0 && NaryOperators.Contains(value[0]);
    }

    private static bool HasTopLevelDivision(string value)
    {
        var depth = 0;
        var inQuotedText = false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '"')
            {
                if (
                    inQuotedText
                    && index + 1 < value.Length
                    && value[index + 1] == '"'
                )
                {
                    index++;
                    continue;
                }
                inQuotedText = !inQuotedText;
                continue;
            }
            if (inQuotedText)
            {
                continue;
            }
            if (character is '(' or '[' or '{' or '〖')
            {
                depth++;
            }
            else if (character is ')' or ']' or '}' or '〗')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (character == '/' && depth == 0)
            {
                return true;
            }
        }
        return false;
    }

    private static bool EndsWithIdentifierOrStructuredFactor(StringBuilder value)
    {
        if (value.Length == 0)
        {
            return false;
        }
        var character = value[^1];
        return char.IsLetter(character)
            || character is ')' or ']' or '}' or '〗';
    }

    private static bool EndsWithStructuredFactor(StringBuilder value) =>
        value.Length > 0 && value[^1] is ')' or ']' or '}' or '〗';

    private static string NormalizeSpaces(string value)
    {
        var output = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value)
        {
            if (character is CaseColumnSpace or TextBoundarySpace)
            {
                pendingSpace = false;
                output.Append(character);
                continue;
            }
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = output.Length > 0;
                continue;
            }
            if (
                pendingSpace
                && output.Length > 0
                && output[^1] != WordLinearMathNormalizer.DifferentialD
                && output[^1]
                    is not ('(' or '[' or '{' or '@' or '&' or NaryBodySeparator)
                && character is not (')' or ']' or '}' or '@' or '&' or ',' or ';')
            )
            {
                output.Append(' ');
            }
            output.Append(character);
            pendingSpace = false;
        }
        return output.ToString().Trim();
    }

    private static NativeToolException Invalid(string message, object? details = null)
    {
        return new NativeToolException("EQUATION_INVALID", message, details);
    }
}
