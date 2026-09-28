' BrowserForWP — inline flow: words into line boxes, then alignment.
'
' A line is broken at whitespace and nowhere else, which is what CSS does by
' default and what keeps a long URL readable instead of split. A word wider than
' the line therefore overflows rather than breaking: overflow is honest, a broken
' word is a lie about the text.
'
' One TextRun per word, not one per line. The renderer draws words at absolute
' positions, so a line box is a container with no visual of its own, and the space
' between two words is simply the gap between their positions.

Namespace Engine.Native

    ''' <summary>Turns inline runs into line boxes. Never throws.</summary>
    Public NotInheritable Class InlineLayout

        Private Sub New()
        End Sub

        ''' <summary>One word and the style it must be drawn in.</summary>
        Private NotInheritable Class WordRun
            Public Property Text As String = String.Empty
            Public Property Style As ComputedStyle
        End Class

        ''' <summary>
        ''' Break runs into line boxes, positioned absolutely. Returns an empty list
        ''' when there is nothing to break or the content box has no width.
        ''' </summary>
        Public Shared Function BuildLines(runs As IList(Of BoxNode), contentLeftPx As Double, flowTopPx As Double, contentWidthPx As Double, containerStyle As ComputedStyle, measurer As ITextMeasurer) As IList(Of LayoutBox)
            Dim lines As New List(Of LayoutBox)()
            If runs Is Nothing OrElse measurer Is Nothing Then Return lines
            If contentWidthPx <= 0 Then Return lines

            Dim words As New List(Of WordRun)()
            For Each runNode In runs
                If runNode Is Nothing OrElse runNode.Style Is Nothing Then Continue For
                For Each wordText In SplitWords(runNode.Text)
                    Dim word As New WordRun()
                    word.Text = wordText
                    word.Style = runNode.Style
                    words.Add(word)
                Next
            Next
            If words.Count = 0 Then Return lines

            ' A line box is as tall as the block that holds it: that is what a
            ' resolved line-height on the container already means.
            Dim lineStyle As ComputedStyle = containerStyle
            If lineStyle Is Nothing Then lineStyle = words(0).Style
            Dim lineHeightPx As Double = measurer.LineHeight(lineStyle)

            Dim lineWords As New List(Of WordRun)()
            Dim lineWidthPx As Double = 0
            Dim lineTopPx As Double = flowTopPx

            For Each word In words
                Dim wordWidthPx As Double = measurer.MeasureWidth(word.Text, word.Style)
                Dim spaceWidthPx As Double = 0
                If lineWords.Count > 0 Then spaceWidthPx = measurer.MeasureWidth(" ", word.Style)

                If lineWords.Count > 0 AndAlso lineWidthPx + spaceWidthPx + wordWidthPx > contentWidthPx Then
                    lines.Add(BuildLine(lineWords, lineWidthPx, lineHeightPx, contentLeftPx, lineTopPx, contentWidthPx, lineStyle, measurer))
                    lineTopPx += lineHeightPx
                    lineWords = New List(Of WordRun)()
                    lineWidthPx = 0
                Else
                    lineWidthPx += spaceWidthPx
                End If

                lineWords.Add(word)
                lineWidthPx += wordWidthPx
            Next

            If lineWords.Count > 0 Then
                lines.Add(BuildLine(lineWords, lineWidthPx, lineHeightPx, contentLeftPx, lineTopPx, contentWidthPx, lineStyle, measurer))
            End If

            Return lines
        End Function

        ''' <summary>Position one line's words, honouring text-align.</summary>
        Private Shared Function BuildLine(lineWords As IList(Of WordRun), lineWidthPx As Double,
                                         lineHeightPx As Double, contentLeftPx As Double,
                                         lineTopPx As Double, contentWidthPx As Double,
                                         lineStyle As ComputedStyle, measurer As ITextMeasurer) As LayoutBox
            Dim lineBox As New LayoutBox()
            lineBox.Kind = LayoutBoxKind.Line
            lineBox.Style = lineStyle
            lineBox.XPx = contentLeftPx
            lineBox.YPx = lineTopPx
            lineBox.WidthPx = contentWidthPx
            lineBox.HeightPx = lineHeightPx

            Dim offsetPx As Double = 0
            If lineStyle IsNot Nothing Then
                If lineStyle.TextAlign = "center" Then
                    offsetPx = (contentWidthPx - lineWidthPx) / 2
                ElseIf lineStyle.TextAlign = "right" Then
                    offsetPx = contentWidthPx - lineWidthPx
                End If
            End If
            If offsetPx < 0 Then offsetPx = 0

            Dim cursorX As Double = contentLeftPx + offsetPx
            Dim isFirstWord As Boolean = True
            For Each word In lineWords
                If Not isFirstWord Then cursorX += measurer.MeasureWidth(" ", word.Style)
                isFirstWord = False

                Dim wordWidthPx As Double = measurer.MeasureWidth(word.Text, word.Style)
                Dim runBox As New LayoutBox()
                runBox.Kind = LayoutBoxKind.TextRun
                runBox.Text = word.Text
                runBox.Style = word.Style
                runBox.XPx = cursorX
                runBox.YPx = lineTopPx
                runBox.WidthPx = wordWidthPx
                runBox.HeightPx = lineHeightPx
                runBox.Parent = lineBox
                lineBox.Children.Add(runBox)

                cursorX += wordWidthPx
            Next

            Return lineBox
        End Function

        ''' <summary>Words of a text run: split on whitespace, empties dropped.</summary>
        Private Shared Function SplitWords(text As String) As IList(Of String)
            Dim words As New List(Of String)()
            If String.IsNullOrEmpty(text) Then Return words

            Dim currentWord As New System.Text.StringBuilder()
            For Each character In text
                If Char.IsWhiteSpace(character) Then
                    If currentWord.Length > 0 Then
                        words.Add(currentWord.ToString())
                        currentWord.Length = 0
                    End If
                Else
                    currentWord.Append(character)
                End If
            Next
            If currentWord.Length > 0 Then words.Add(currentWord.ToString())
            Return words
        End Function

    End Class

End Namespace
