Option Strict On

Namespace Data

    ''' <summary>One comic you own, as shown and edited in the app.</summary>
    Public Class ComicRecord
        Public Property ComicId As Long
        Public Property Series As String = ""
        Public Property Volume As String = ""
        Public Property Issue As String = ""
        Public Property CoverLetter As String = ""
        Public Property VariantName As String = ""
        Public Property Title As String = ""
        Public Property Publisher As String = ""
        Public Property CoverDate As String = ""
        Public Property Format As String = ""
        Public Property Barcode As String = ""
        Public Property MetronId As Long?
        Public Property CoverUrl As String = ""
        Public Property CoverPrice As Double?
        Public Property Quantity As Integer = 1
        Public Property Condition As String = ""
        Public Property PricePaid As Double?
        Public Property CurrentValue As Double?
        Public Property PurchaseDate As String = ""
        Public Property Notes As String = ""

        Public Overrides Function ToString() As String
            Dim name = Series & If(Volume <> "", $" ({Volume})", "")
            Return If(Issue <> "", $"{name} #{Issue}{CoverLetter}", name)
        End Function
    End Class

    ''' <summary>One match from Metron.</summary>
    Public Class MetronIssue
        Public Property MetronId As Long
        Public Property Series As String = ""
        Public Property Volume As String = ""
        Public Property Number As String = ""
        Public Property Title As String = ""
        Public Property Publisher As String = ""
        Public Property CoverDate As String = ""
        Public Property CoverUrl As String = ""
        ''' <summary>Original cover price. Search results don't include it; full issue details do.</summary>
        Public Property Price As Double?

        Public Overrides Function ToString() As String
            Dim name = Series & If(Volume <> "", $" ({Volume})", "")
            Return $"{name} #{Number}" & If(CoverDate <> "", $"  ·  {CoverDate}", "") & If(Title <> "", $"  ·  {Title}", "")
        End Function
    End Class

    Public Class CollectionStats
        Public Property Comics As Integer
        Public Property Copies As Integer
        Public Property TotalValue As Double
        Public Property TotalPaid As Double
    End Class

End Namespace
