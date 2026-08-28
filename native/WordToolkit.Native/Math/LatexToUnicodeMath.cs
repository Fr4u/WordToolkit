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
    private const char AboveMarker = '┴';
    private const char CaseColumnSpace = WordMathSpacing.CaseColumn;
    private const char TextBoundarySpace = WordMathSpacing.TextBoundary;
    private const char InvisibleTimes = '\u2062';
    private const char MiddleDelimiterMarker = '║';

    private static readonly HashSet<char> NaryOperators =
    [
        '∑',
        '∏',
        '∐',
        '∫',
        '∬',
        '∭',
        '⨌',
        '∮',
        '∱',
        '∲',
        '∳',
        '∯',
        '∰',
        '⋃',
        '⋂',
        '⨁',
        '⨂',
        '⨀',
        '⨄',
        '⨆',
        '⨅',
        '⋁',
        '⋀',
    ];



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
        "Pr",
        "asin",
        "acos",
        "atan",
        "arg",
        "ker",
        "dim",
        "deg",
        "inf",
        "sup",
        "mod",
        "limsup", "liminf", "hom", "End", "rank", "tr", "sgn", "erf",
    ];

    private static readonly HashSet<string> QuotedOperators =
    [
        "min",
        "max",
        "det",
        "gcd",
        "limsup",
        "liminf",
        "inf",
        "sup",
        "hom", "End", "rank", "tr", "sgn", "erf",
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
        private readonly Stack<string> _leftDelimiters = new();

        public Parser(string source)
        {
            _source = source;
        }

        public string ParseAll()
        {
            var value = ParseSequence(null).Trim();
            if (_leftDelimiters.Count != 0)
            {
                throw Invalid(
                    "A LaTeX \\left delimiter has no matching \\right",
                    new { unmatched_left_count = _leftDelimiters.Count }
                );
            }
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
                    if (character == '\\' && TryConsumeCommand("choose"))
                    {
                        var numerator = output.ToString().Trim();
                        if (numerator.Length == 0)
                        {
                            throw Invalid(
                                "\\choose requires a numerator before the command",
                                new { index = _index - @"\choose".Length }
                            );
                        }
                        SkipWhitespace();
                        var denominator = ParseSequence(terminator).Trim();
                        if (denominator.Length == 0)
                        {
                            throw Invalid(
                                "\\choose requires a denominator after the command",
                                new { index = _index }
                            );
                        }
                        return $"({numerator}¦{denominator})";
                    }
                    var baseAtom = ParseAtom();
                    if (baseAtom.Length == 0)
                    {
                        continue;
                    }
                    var atom = ParseScripts(baseAtom);
                    if (
                        IsUpperStretch(baseAtom)
                        && atom.StartsWith(baseAtom + "^(", StringComparison.Ordinal)
                    )
                    {
                        atom = baseAtom + AboveMarker + atom[(baseAtom.Length + 1)..];
                    }
                    else if (
                        IsLowerStretch(baseAtom)
                        && atom.StartsWith(baseAtom + "_(", StringComparison.Ordinal)
                    )
                    {
                        atom = baseAtom + BelowMarker + atom[(baseAtom.Length + 1)..];
                    }
                    if (
                        IsLowerLimitOperator(baseAtom)
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
                        && HasFollowingNaryBody(terminator)
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
                // TeX uses a braced group to keep punctuation attached to a
                // number (most notably the decimal comma in `3{,}14`).
                // Braces are syntax here, not visible delimiters; wrapping
                // this one-character punctuation group in parentheses changes
                // the equation to `3(,)14`.
                var trimmed = body.Trim();
                return trimmed == "," ? trimmed : $"({trimmed})";
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
                && HasFollowingNaryBody(null)
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
            if (command is "binom" or "dbinom" or "tbinom")
            {
                var upper = ParseRequiredGroup(command);
                var lower = ParseRequiredGroup(command);
                return $"({upper.Trim()}¦{lower.Trim()})";
            }
            if (command is "cbrt" or "qdrt")
            {
                var body = ParseRequiredGroup(command).Trim();
                return command == "cbrt" ? $"√(3&{body})" : $"√(4&{body})";
            }
            if (command == "root")
            {
                SkipWhitespace();
                var degree = _index < _source.Length && _source[_index] == '{'
                    ? ParseRequiredGroup(command).Trim()
                    : ParseUnbracedRootArgument(command);
                SkipWhitespace();
                if (!TryConsumeCommand("of"))
                    throw Invalid("\\root requires \\of");
                SkipWhitespace();
                var body = _index < _source.Length && _source[_index] == '{'
                    ? ParseRequiredGroup(command).Trim()
                    : ParseUnbracedRootArgument(command);
                return $"√({degree}&{body})";
            }
            if (command is "phantom" or "hphantom" or "vphantom" or "smash" or "hsmash" or "asmash" or "dsmash")
            {
                var body = ParseRequiredGroup(command).Trim();
                // UTN 28 section 3.17 assigns one exact enclosure marker to
                // each phantom/smash geometry. These are Word UnicodeMath
                // controls, not visible approximation characters.
                var marker = command switch
                {
                    "phantom" => "⟡",
                    "hphantom" => "⬄",
                    "vphantom" => "⇳",
                    "smash" => "⬍",
                    "hsmash" => "⬌",
                    "asmash" => "⬆",
                    _ => "⬇",
                };
                return $"{marker}({body})";
            }
            if (command == "middle")
            {
                if (_leftDelimiters.Count == 0)
                {
                    throw Invalid("\\middle is only valid between matching \\left and \\right delimiters");
                }
                SkipWhitespace();
                return MiddleDelimiterMarker + ParseDelimiter(command);
            }
            if (command is "mod" or "bmod" or "pmod")
            {
                SkipWhitespace();
                if (_index >= _source.Length || _source[_index] == '}')
                {
                    throw Invalid($"\\{command} requires a modulus");
                }
                var body = _index < _source.Length && _source[_index] == '{'
                    ? ParseRequiredGroup(command).Trim()
                    : ParseAtom().Trim();
                return command switch
                {
                    "mod" => $" mod {body}",
                    "bmod" => $" mod {body}",
                    _ => $"(mod {body})",
                };
            }
            if (command is "overset" or "stackrel" or "underset")
            {
                var annotation = ParseRequiredGroup(command).Trim();
                var body = ParseRequiredGroup(command).Trim();
                var marker = command == "underset" ? BelowMarker : AboveMarker;
                return $"{ParenthesizeBase(body)}{marker}({annotation})";
            }
            if (
                command
                    is "overbrace"
                        or "underbrace"
                        or "overparen"
                        or "underparen"
                        or "overbracket"
                        or "underbracket"
            )
            {
                var body = ParseRequiredGroup(command).Trim();
                var character = command switch
                {
                    "overbrace" => '⏞',
                    "underbrace" => '⏟',
                    "overparen" => '⏜',
                    "underparen" => '⏝',
                    "overbracket" => '⎴',
                    _ => '⎵',
                };
                return $"{character}({body})";
            }
            if (command == "substack")
            {
                var raw = ReadRequiredRawGroup(command);
                var rows = SplitTopLevel(raw, rowSeparator: true);
                if (rows.Count == 0) throw Invalid("\\substack is empty");
                var converted = rows.Select(row => new Parser(row).ParseAll());
                return $"{EquationArrayMarker}({string.Join("@", converted)})";
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
            if (command == "boxed")
            {
                var body = ParseRequiredGroup(command);
                return $"▭({body.Trim()})";
            }
            if (command is "bra" or "ket" or "expectation")
            {
                var body = ParseRequiredGroup(command).Trim();
                return command switch
                {
                    "bra" => $"⟨{body}∣",
                    "ket" => $"∣{body}⟩",
                    _ => $"⟨{body}⟩",
                };
            }
            if (command == "braket")
            {
                var left = ParseRequiredGroup(command).Trim();
                SkipWhitespace();
                if (_index < _source.Length && _source[_index] == '{')
                {
                    var right = ParseRequiredGroup(command).Trim();
                    return $"⟨{left}∣{right}⟩";
                }
                return $"⟨{left.Replace("|", "∣", StringComparison.Ordinal)}⟩";
            }
            if (command == "matrixel")
            {
                var left = ParseRequiredGroup(command).Trim();
                var @operator = ParseRequiredGroup(command).Trim();
                var right = ParseRequiredGroup(command).Trim();
                return $"⟨{left}∣{@operator}∣{right}⟩";
            }
            if (command is "dv" or "pdv")
            {
                var first = ParseRequiredGroup(command).Trim();
                SkipWhitespace();
                var differential = command == "dv"
                    ? WordLinearMathNormalizer.DifferentialD.ToString()
                    : "∂";
                if (_index < _source.Length && _source[_index] == '{')
                {
                    var variable = ParseRequiredGroup(command).Trim();
                    return $"({differential} {first})/({differential} {variable})";
                }
                return $"({differential})/({differential} {first})";
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
                    or "widehat"
                    or "bar"
                    or "overline"
                    or "tilde"
                    or "dot"
                    or "ddot"
                    or "acute" or "grave" or "dddot" or "widetilde" or "underbar"
                    or "check"
                    or "breve"
                    or "underline"
                    or "overrightarrow"
                    or "overleftarrow"
            )
            {
                var body = ParseRequiredGroup(command);
                var accent = command switch
                {
                    "overline" => "bar",
                    "widehat" => "hat",
                    "overrightarrow" => "vec",
                    "overleftarrow" => "vec",
                    "widetilde" => "tilde",
                    "underbar" => "underline",
                    _ => command,
                };
                return ApplyAccent(body.Trim(), accent);
            }
            if (command is "left" or "right")
            {
                if (command == "right" && _leftDelimiters.Count == 0)
                {
                    throw Invalid("A LaTeX \\right delimiter has no matching \\left");
                }
                SkipWhitespace();
                var parsedDelimiter = ParseDelimiter(command);
                if (command == "left")
                {
                    _leftDelimiters.Push(parsedDelimiter);
                }
                else
                {
                    _leftDelimiters.Pop();
                }
                return parsedDelimiter;
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
            if (LatexSymbolCatalog.Symbols.TryGetValue(command, out var symbol))
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
                    !char.IsLetterOrDigit(character)
                    && !char.IsWhiteSpace(character)
                    && character is not ('/' or '·' or '⋅' or '.' or ',' or '-' or '%' or '°')
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
                        or "split"
                        or "multline"
                        or "equation"
                        or "smallmatrix"
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
            if (supported == "equation")
            {
                return new Parser(body).ParseAll();
            }
            if (
                supported
                    is "aligned"
                        or "align"
                        or "gathered"
                        or "split"
                        or "multline"
                        or "cases"
            )
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
                "smallmatrix" => $"{MatrixMarker}({matrixBody})",
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

        private string ParseUnbracedRootArgument(string command)
        {
            if (
                _index >= _source.Length
                || _source[_index] == '\\'
                || char.IsWhiteSpace(_source[_index])
            )
            {
                throw Invalid(
                    $"\\{command} requires a braced argument when the argument is not one character",
                    new { index = _index }
                );
            }
            var atom = ParseAtom();
            return ParseScripts(atom).Trim();
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

        private bool HasFollowingNaryBody(char? terminator)
        {
            var index = _index;
            while (index < _source.Length && char.IsWhiteSpace(_source[index]))
            {
                index++;
            }
            if (index >= _source.Length)
            {
                return false;
            }
            if (_source[index] == NaryBodySeparator)
            {
                return false;
            }
            if (
                _source.AsSpan(index).StartsWith(@"\right", StringComparison.Ordinal)
                && (
                    index + 6 >= _source.Length
                    || !char.IsLetter(_source[index + 6])
                )
            )
            {
                return false;
            }
            if (terminator is not null && _source[index] == terminator)
            {
                return false;
            }
            return _source[index] is not (')' or ']' or '}');
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
            && atom.Length > 0
            && atom[0] is '∂' or '∇'
            && (atom.Contains("_(", StringComparison.Ordinal)
                || atom.Contains("^(", StringComparison.Ordinal))
            && EndsWithScript(output)
        )
        {
            // Word can merge `γ^μ∂_μ` into one malformed sub/superscript
            // object. U+2062 is the UnicodeMath implicit-times boundary; the
            // readback verifier deliberately treats it as semantic
            // juxtaposition, while Word keeps the two scripted factors apart.
            output.Append(InvisibleTimes);
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

    private static bool EndsWithScript(StringBuilder value)
    {
        if (value.Length < 4 || value[^1] != ')')
        {
            return false;
        }
        var depth = 1;
        for (var index = value.Length - 2; index >= 0; index--)
        {
            if (value[index] == ')')
            {
                depth++;
            }
            else if (value[index] == '(' && --depth == 0)
            {
                return index > 0 && value[index - 1] is '^' or '_';
            }
        }
        return false;
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
            "acute" => "\u0301",
            "grave" => "\u0300",
            "dddot" => "\u20DB",
            "check" => "\u030C",
            "breve" => "\u0306",
            "underline" => "\u0332",
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

    private static bool IsUpperStretch(string value) =>
        value.Length > 0 && value[0] is '⏜' or '⏞' or '⏠' or '⎴';

    private static bool IsLowerStretch(string value) =>
        value.Length > 0 && value[0] is '⏝' or '⏟' or '⏡' or '⎵';

    private static bool IsLowerLimitOperator(string value) =>
        value
            is "lim"
                or "\"min\""
                or "\"max\""
                or "\"limsup\""
                or "\"liminf\""
                or "\"inf\""
                or "\"sup\"";

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
