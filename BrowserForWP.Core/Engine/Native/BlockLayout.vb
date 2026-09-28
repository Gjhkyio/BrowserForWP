' BrowserForWP — block layout: a box tree in, positioned boxes out.
'
' It answers three questions per box -- how wide, how tall, where -- for static,
' non-floated, non-positioned blocks that stack vertically. Inline flow is handed
' to InlineLayout; this class never measures text itself.
'
' Deliberately absent, each for a stated reason:
'   * float and position      -- outside the subset this repository declares
'   * auto margins            -- centring waits until a real page needs it
'   * margin collapsing       -- a refinement; without it spacing is slightly
'                                wide, which is visible but not wrong
'   * box-sizing / min-height -- content box only
'
' The node the page is laid out in is synthetic: Layout returns a #page box whose
' own style is Nothing, so its content box is the whole viewport and the html box
' is laid out inside it like any other child.

Namespace Engine.Native

    ''' <summary>Turns a box tree into a positioned tree. Never throws.</summary>
    Public NotInheritable Class BlockLayout

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Lay root out inside a viewport viewportWidthPx wide. Returns Nothing when
        ''' there is nothing to lay out or the viewport has no width.
        ''' </summary>
        Public Shared Function Layout(root As BoxNode, viewportWidthPx As Double, measurer As ITextMeasurer) As LayoutBox
            If root Is Nothing OrElse measurer Is Nothing Then Return Nothing
            If viewportWidthPx <= 0 Then Return Nothing

            Dim page As New LayoutBox()
            page.Kind = LayoutBoxKind.Block
            page.TagName = "#page"
            page.Style = Nothing
            page.XPx = 0
            page.YPx = 0
            page.WidthPx = viewportWidthPx

            Dim consumed As Double = LayOutChildren(root, page, viewportWidthPx, measurer)
            page.HeightPx = consumed
            Return page
        End Function

        ''' <summary>
        ''' Walk one node's children inside its content box and return the height
        ''' they consumed. Consecutive inline children form one flow, because a run of
        ''' text and a span between two paragraphs is one line-breaking problem, not
        ''' one problem each.
        ''' </summary>
        Private Shared Function LayOutChildren(parentNode As BoxNode, parentBox As LayoutBox,
                                              contentWidthPx As Double, measurer As ITextMeasurer) As Double
            Dim flowTopPx As Double = parentBox.ContentTopPx
            Dim consumed As Double = 0
            Dim runGroup As New List(Of BoxNode)()

            For Each childNode In parentNode.Children
                If childNode Is Nothing Then Continue For
                If childNode.Kind = BoxKind.Block Then
                    If runGroup.Count > 0 Then
                        consumed += LayOutInlineRun(runGroup, parentBox, flowTopPx + consumed, contentWidthPx, measurer)
                        runGroup.Clear()
                    End If
                    Dim childBox As LayoutBox = LayOutBlock(childNode, parentBox.ContentLeftPx,
                                                           flowTopPx + consumed, contentWidthPx, measurer)
                    If childBox IsNot Nothing AndAlso childBox.Style IsNot Nothing Then
                        parentBox.Children.Add(childBox)
                        consumed += Math.Max(0, childBox.Style.MarginTopPx) + childBox.HeightPx +
                                    Math.Max(0, childBox.Style.MarginBottomPx)
                    End If
                Else
                    runGroup.Add(childNode)
                End If
            Next

            If runGroup.Count > 0 Then
                consumed += LayOutInlineRun(runGroup, parentBox, flowTopPx + consumed, contentWidthPx, measurer)
            End If

            Return consumed
        End Function

        ''' <summary>Position one block box and lay out its children.</summary>
        Private Shared Function LayOutBlock(node As BoxNode, parentContentLeftPx As Double,
                                            flowTopPx As Double, containingWidthPx As Double,
                                            measurer As ITextMeasurer) As LayoutBox
            Dim nodeStyle As ComputedStyle = node.Style
            If nodeStyle Is Nothing Then Return Nothing

            Dim marginLeft As Double = Math.Max(0, nodeStyle.MarginLeftPx)
            Dim marginRight As Double = Math.Max(0, nodeStyle.MarginRightPx)
            Dim available As Double = containingWidthPx - marginLeft - marginRight

            Dim borderBoxWidth As Double
            If nodeStyle.WidthPx >= 0 Then
                borderBoxWidth = nodeStyle.WidthPx
            Else
                borderBoxWidth = available
            End If
            If nodeStyle.MaxWidthPx >= 0 AndAlso borderBoxWidth > nodeStyle.MaxWidthPx Then
                borderBoxWidth = nodeStyle.MaxWidthPx
            End If
            If borderBoxWidth > available Then borderBoxWidth = available
            If borderBoxWidth < 0 Then borderBoxWidth = 0

            Dim box As New LayoutBox()
            box.Kind = LayoutBoxKind.Block
            box.TagName = node.TagName
            box.Style = nodeStyle
            box.XPx = parentContentLeftPx + marginLeft
            box.YPx = flowTopPx + Math.Max(0, nodeStyle.MarginTopPx)
            box.WidthPx = borderBoxWidth

            ' A box with no content width cannot hold children. Recursing would still
            ' produce correctly positioned children at width zero, which on a 480px
            ' screen is noise rather than a layout.
            Dim contentHeightPx As Double = 0
            If box.ContentWidthPx > 0 Then
                contentHeightPx = LayOutChildren(node, box, box.ContentWidthPx, measurer)
            End If

            Dim borderBoxHeight As Double = Math.Max(0, nodeStyle.BorderTopWidthPx) +
                                            Math.Max(0, nodeStyle.PaddingTopPx) + contentHeightPx +
                                            Math.Max(0, nodeStyle.PaddingBottomPx) +
                                            Math.Max(0, nodeStyle.BorderBottomWidthPx)
            If nodeStyle.HeightPx >= 0 AndAlso nodeStyle.HeightPx > borderBoxHeight Then
                borderBoxHeight = nodeStyle.HeightPx
            End If
            box.HeightPx = borderBoxHeight
            Return box
        End Function

        ''' <summary>
        ''' Inline flow: delegate the run group to InlineLayout and add the lines to
        ''' the parent. Kept as one function so the block path never learns how lines
        ''' are built.
        ''' </summary>
        Private Shared Function LayOutInlineRun(runs As IList(Of BoxNode), parentBox As LayoutBox,
                                               flowTopPx As Double, contentWidthPx As Double,
                                               measurer As ITextMeasurer) As Double
            Dim lines As IList(Of LayoutBox) = InlineLayout.BuildLines(runs, parentBox.ContentLeftPx,
                                                                       flowTopPx, contentWidthPx,
                                                                       parentBox.Style, measurer)
            Dim usedPx As Double = 0
            For Each lineBox In lines
                lineBox.Parent = parentBox
                parentBox.Children.Add(lineBox)
                usedPx += lineBox.HeightPx
            Next
            Return usedPx
        End Function

    End Class

End Namespace
