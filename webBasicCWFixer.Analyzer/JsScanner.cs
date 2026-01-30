namespace webBasicCWFixer.Analyzer;

internal sealed class JsScanner
{
    private readonly string _s;
    private int _i;

    public JsTokenKind Kind { get; private set; }
    public string Text { get; private set; } = "";
    public int StartIndex { get; private set; }
    public int Line { get; private set; } = 1;
    public int Column { get; private set; } = 1;

    private int _line = 1;
    private int _col = 1;

    private JsTokenKind _prevNonTriviaKind = JsTokenKind.Unknown;
    private string _prevNonTriviaText = "";

    public JsScanner(string s)
    {
        _s = s ?? "";
        _i = 0;
    }

    public bool MoveNext() => ReadNextToken(includeTrivia: true);

    public bool MoveNextNonTrivia()
    {
        while (ReadNextToken(includeTrivia: true))
        {
            if (Kind != JsTokenKind.Trivia) return true;
        }
        return false;
    }

    public bool PrevNonTriviaIsDot()
        => _prevNonTriviaKind == JsTokenKind.Punct && _prevNonTriviaText == ".";

    public bool NextNonTriviaIsColon()
    {
        var snap = Snapshot();
        try
        {
            if (!MoveNextNonTrivia()) return false;
            return Kind == JsTokenKind.Punct && Text == ":";
        }
        finally
        {
            Restore(snap);
        }
    }

    public ScannerSnapshot Snapshot() => new(_i, _line, _col, _prevNonTriviaKind, _prevNonTriviaText);

    public void Restore(ScannerSnapshot s)
    {
        _i = s.I;
        _line = s.Line;
        _col = s.Col;
        _prevNonTriviaKind = s.PrevKind;
        _prevNonTriviaText = s.PrevText;
        Kind = JsTokenKind.Unknown;
        Text = "";
        StartIndex = 0;
        Line = _line;
        Column = _col;
    }

    private bool ReadNextToken(bool includeTrivia)
    {
        if (_i >= _s.Length) return false;

        StartIndex = _i;
        Line = _line;
        Column = _col;

        char c = _s[_i];

        if (char.IsWhiteSpace(c))
        {
            var start = _i;
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i]))
            {
                Advance(_s[_i]);
                _i++;
            }
            Kind = JsTokenKind.Trivia;
            Text = _s.Substring(start, _i - start);
            return includeTrivia;
        }

        if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '/')
        {
            var start = _i;
            _i += 2;
            _col += 2;
            while (_i < _s.Length && _s[_i] != '\n')
            {
                Advance(_s[_i]);
                _i++;
            }
            Kind = JsTokenKind.Trivia;
            Text = _s.Substring(start, _i - start);
            return includeTrivia;
        }

        if (c == '/' && _i + 1 < _s.Length && _s[_i + 1] == '*')
        {
            var start = _i;
            _i += 2;
            _col += 2;
            while (_i < _s.Length)
            {
                if (_s[_i] == '*' && _i + 1 < _s.Length && _s[_i + 1] == '/')
                {
                    _i += 2;
                    _col += 2;
                    break;
                }
                Advance(_s[_i]);
                _i++;
            }
            Kind = JsTokenKind.Trivia;
            Text = _s.Substring(start, _i - start);
            return includeTrivia;
        }

        if (c is '\'' or '"' or '`')
        {
            var quote = c;
            var start = _i;
            Advance(c);
            _i++;
            while (_i < _s.Length)
            {
                var ch = _s[_i];
                if (ch == '\\')
                {
                    Advance(ch);
                    _i++;
                    if (_i < _s.Length)
                    {
                        Advance(_s[_i]);
                        _i++;
                    }
                    continue;
                }
                if (ch == quote)
                {
                    Advance(ch);
                    _i++;
                    break;
                }
                Advance(ch);
                _i++;
            }
            Kind = JsTokenKind.String;
            Text = _s.Substring(start, _i - start);
            UpdatePrevNonTrivia();
            return true;
        }

        if (IsIdentStart(c))
        {
            var start = _i;
            Advance(c);
            _i++;
            while (_i < _s.Length && IsIdentPart(_s[_i]))
            {
                Advance(_s[_i]);
                _i++;
            }
            Kind = JsTokenKind.Identifier;
            Text = _s.Substring(start, _i - start);
            UpdatePrevNonTrivia();
            return true;
        }

        if (char.IsDigit(c))
        {
            var start = _i;
            Advance(c);
            _i++;
            while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.'))
            {
                Advance(_s[_i]);
                _i++;
            }
            Kind = JsTokenKind.Number;
            Text = _s.Substring(start, _i - start);
            UpdatePrevNonTrivia();
            return true;
        }

        Kind = JsTokenKind.Punct;
        Text = c.ToString();
        Advance(c);
        _i++;
        UpdatePrevNonTrivia();
        return true;
    }

    private void UpdatePrevNonTrivia()
    {
        if (Kind == JsTokenKind.Trivia) return;
        _prevNonTriviaKind = Kind;
        _prevNonTriviaText = Text;
    }

    private void Advance(char c)
    {
        if (c == '\n')
        {
            _line++;
            _col = 1;
        }
        else
        {
            _col++;
        }
    }

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_' || c == '$';
    private static bool IsIdentPart(char c) => IsIdentStart(c) || char.IsDigit(c);
}

internal sealed record LintIssue(
    string FullName,
    string Rule,
    string Message,
    int Line,
    int Column
);

internal enum JsTokenKind { Trivia, Identifier, Number, String, Punct, Unknown }

internal readonly record struct ScannerSnapshot(int I, int Line, int Col, JsTokenKind PrevKind, string PrevText);
