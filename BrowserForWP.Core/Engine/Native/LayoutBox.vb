' BrowserForWP — one positioned node.
'
' Coordinate contract, relied on by the renderer and by InlineLayout:
'   * XPx / YPx            the BORDER BOX's top-left. Margins are outside it.
'   * WidthPx / HeightPx   the border box, including border and padding.
'   * ContentLeftPx / ContentTopPx / ContentWidthPx   where children live.
'
' Margins are therefore never inside a rectangle: they are space, and BlockLayout
' applies them by moving the border box and by advancing the flow cursor.

Namespace Engine.Native

    ''' <summary>What a laid-out node is.</summary>
    Public Enum LayoutBoxKind
        ''' <summary>A block box: background, border, and children.</summary>
        Block
        ''' <summary>A line box inside a block. Children are its text runs.</summary>
        Line
        ''' <summary>A run of text to draw at its own position.</summary>
        TextRun
    End Enum

    ''' <summary>A node with geometry. All values are device-independent pixels.</summary>
    Public NotInheritable Class LayoutBox

        Public Property Kind As LayoutBoxKind
        Public Property TagName As String = String.Empty
        Public Property Text As String = String.Empty
        Public Property Style As ComputedStyle
        Public Property Children As New List(Of LayoutBox)()
        Public Property Parent As LayoutBox

        ''' <summary>Left edge of the border box.</summary>
        Public Property XPx As Double
        ''' <summary>Top edge of the border box.</summary>
        Public Property YPx As Double
        ''' <summary>Border-box width.</summary>
        Public Property WidthPx As Double
        ''' <summary>Border-box height.</summary>
        Public Property HeightPx As Double

        ''' <summary>Left edge of the content box.</summary>
        Public ReadOnly Property ContentLeftPx As Double
            Get
                If Style Is Nothing Then Return XPx
                Return XPx + Math.Max(0, Style.BorderLeftWidthPx) + Math.Max(0, Style.PaddingLeftPx)
            End Get
        End Property

        ''' <summary>Top edge of the content box.</summary>
        Public ReadOnly Property ContentTopPx As Double
            Get
                If Style Is Nothing Then Return YPx
                Return YPx + Math.Max(0, Style.BorderTopWidthPx) + Math.Max(0, Style.PaddingTopPx)
            End Get
        End Property

        ''' <summary>Width available to children.</summary>
        Public ReadOnly Property ContentWidthPx As Double
            Get
                If Style Is Nothing Then Return WidthPx
                Dim inset As Double = Math.Max(0, Style.BorderLeftWidthPx) + Math.Max(0, Style.BorderRightWidthPx) +
                                      Math.Max(0, Style.PaddingLeftPx) + Math.Max(0, Style.PaddingRightPx)
                Dim usable As Double = WidthPx - inset
                If usable < 0 Then Return 0
                Return usable
            End Get
        End Property

        ''' <summary>Depth-first count, used by the diagnostics readout.</summary>
        Public Function DescendantCount() As Integer
            Dim total As Integer = Children.Count
            For Each childItem In Children
                total += childItem.DescendantCount()
            Next
            Return total
        End Function

    End Class

End Namespace
