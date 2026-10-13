Option Strict On

Imports System.Text.RegularExpressions

Namespace Data

    ''' <summary>
    ''' Search the way people type it: "daredevil 50", "walking dead 100", "spiderman", "#50a". Every word has to
    ''' be found somewhere. A number has to be the issue number (any cover letter, leading zeros or not) or a whole
    ''' number elsewhere, such as a volume year, so "daredevil 50" doesn't bring up #150 or #500. Words in quotes
    ''' are found together ("i am gotham"). The same rules as the phone app's search (js/search.js).
    ''' </summary>
    Public Module WordSearch

        Private ReadOnly NumberWord As New Regex("^0*([0-9]+)([a-z]{0,2})$")
        Private ReadOnly Pieces As New Regex("""([^""]*)""?|[^\s,""]+")

        ''' <summary>The words of a search: lower case, without "#" and without "the" (typed or not).</summary>
        Public Function WordsOf(query As String) As List(Of String)
            Dim words As New List(Of String)
            For Each m As Match In Pieces.Matches(If(query, "").ToLowerInvariant())
                If m.Groups(1).Success Then
                    Dim phrase = Regex.Replace(m.Groups(1).Value, "\s+", " ").Trim()
                    If phrase <> "" Then words.Add(phrase)
                Else
                    Dim w = m.Value.TrimStart("#"c)
                    If w <> "" AndAlso w <> "the" Then words.Add(w)
                End If
            Next
            Return words
        End Function

        Private Function Squash(s As String) As String
            Return Regex.Replace(s, "[^a-z0-9]", "")
        End Function

        ''' <summary>A comic's issue number as digits and cover letter: "050A" is ("50a", "50", "a").</summary>
        Private Function IssueParts(number As String, letter As String) As (Issue As String, Digits As String, Letter As String)
            Dim issue = If(number, "").Trim().ToLowerInvariant()
            If issue.StartsWith("#", StringComparison.Ordinal) Then issue = issue.Substring(1)
            issue = Regex.Replace(issue, "^0+(?=[0-9])", "")
            Dim digits = Regex.Replace(issue, "[a-z]+$", "")
            Dim cover = issue.Substring(digits.Length)
            If cover = "" Then cover = If(letter, "").Trim().ToLowerInvariant()
            Return (issue, digits, cover)
        End Function

        ''' <summary>Whether a number word is this issue: "50" is #50 (any cover), "50a" only cover A.</summary>
        Private Function IsIssue(w As String, issue As (Issue As String, Digits As String, Letter As String)) As Boolean
            Dim m = NumberWord.Match(w)
            Return m.Success AndAlso m.Groups(1).Value = issue.Digits AndAlso (m.Groups(2).Value = "" OrElse m.Groups(2).Value = issue.Letter)
        End Function

        ''' <summary>
        ''' True when every word is found. text: the comic's words (series, volume, publisher…);
        ''' number and letter: its issue number and cover letter, matched exactly.
        ''' </summary>
        Public Function AllFound(words As IList(Of String), text As IEnumerable(Of String), number As String, Optional letter As String = "") As Boolean
            If words.Count = 0 Then Return True
            Dim fields = text.Select(Function(t) If(t, "").ToLowerInvariant()).Where(Function(t) t <> "").ToList()
            Dim squashed = fields.Select(Function(f) Squash(f)).ToList()
            Dim issue = IssueParts(number, letter)
            Return words.All(Function(w) Found(w, fields, squashed, issue))
        End Function

        Private Function Found(w As String, fields As List(Of String), squashed As List(Of String),
                               issue As (Issue As String, Digits As String, Letter As String)) As Boolean
            Dim num = NumberWord.Match(w)
            If num.Success Then
                If IsIssue(w, issue) Then Return True
                If num.Groups(2).Value <> "" Then Return fields.Any(Function(f) f.Contains(w))
                Dim whole = $"(^|[^0-9]){num.Groups(1).Value}($|[^0-9])"
                If fields.Any(Function(f) Regex.IsMatch(f, whole)) Then Return True
                ' Part of a barcode, typed with or without its space.
                Return w.Length >= 5 AndAlso fields.Any(Function(f) Regex.Replace(f, "[^0-9]", "").Contains(w))
            End If
            Dim sw = Squash(w)
            Return issue.Issue.Contains(w) OrElse fields.Any(Function(f) f.Contains(w)) OrElse
                   (sw <> "" AndAlso squashed.Any(Function(f) f.Contains(sw)))
        End Function

        ''' <summary>
        ''' How well a comic that matches fits: 2 when the last number typed is its issue number
        ''' ("harley quinn vol 3 1" is #1), 1 when another number is, otherwise 0.
        ''' </summary>
        Public Function Score(words As IList(Of String), number As String, Optional letter As String = "") As Integer
            Dim numbers = words.Where(Function(w) NumberWord.IsMatch(w)).ToList()
            If numbers.Count = 0 Then Return 0
            Dim issue = IssueParts(number, letter)
            If IsIssue(numbers(numbers.Count - 1), issue) Then Return 2
            Return If(numbers.Any(Function(w) IsIssue(w, issue)), 1, 0)
        End Function

    End Module

End Namespace
