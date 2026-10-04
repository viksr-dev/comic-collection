Option Strict On

Namespace Data

    ''' <summary>
    ''' A comic barcode: a 12-digit UPC (or 13-digit EAN/ISBN) plus a small add-on.
    ''' The 5-digit add-on holds the issue number (3 digits), cover (1) and printing (1),
    ''' e.g. 00111 = issue 1, cover 1, 1st printing.
    ''' </summary>
    Public Class ParsedBarcode
        Public Property Upc As String = ""
        Public Property Addon As String = ""
        Public Property Issue As String = ""
        Public Property Cover As String = ""
        Public Property Printing As String = ""

        Public ReadOnly Property Full As String
            Get
                Return Upc & Addon
            End Get
        End Property

        Public ReadOnly Property IsValid As Boolean
            Get
                Return Upc.Length = 12 OrElse Upc.Length = 13
            End Get
        End Property
    End Class

    Public Module Barcode

        ''' <summary>Splits typed or scanned digits into the main code and add-on. Spaces and dashes are ignored.</summary>
        Public Function Parse(raw As String, Optional typedAddon As String = "") As ParsedBarcode
            Dim digits = OnlyDigits(raw)
            Dim addon = OnlyDigits(typedAddon)
            Dim base = digits

            Dim lengths = {(13, 5), (13, 2), (12, 5), (12, 2), (13, 0), (12, 0)}
            For Each l In lengths
                If digits.Length = l.Item1 + l.Item2 Then
                    base = digits.Substring(0, l.Item1)
                    If l.Item2 > 0 Then addon = digits.Substring(l.Item1)
                    Exit For
                End If
            Next
            ' A 13-digit code starting with 0 is a UPC with a leading zero.
            If base.Length = 13 AndAlso base.StartsWith("0") Then base = base.Substring(1)

            Dim result As New ParsedBarcode With {.Upc = base, .Addon = addon}
            If addon.Length = 5 Then
                result.Issue = CInt(addon.Substring(0, 3)).ToString()
                result.Cover = addon.Substring(3, 1)
                result.Printing = addon.Substring(4, 1)
            End If
            Return result
        End Function

        Public Function OnlyDigits(s As String) As String
            If s Is Nothing Then Return ""
            Return New String(s.Where(Function(ch) Char.IsDigit(ch)).ToArray())
        End Function

    End Module

End Namespace
