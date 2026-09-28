' BrowserForWP — the value types the native pipeline stages agree on.
'
' Types only, no logic: every stage converts one of these into the next, so this
' file is the interface contract between the prototype and the VB. The names here
' are matched by tools/proto/htmlparse.mjs, csscascade.mjs and boxtree.mjs --
' change one, change both.

Imports System.Collections.Generic

Namespace Engine.Native

    Public Enum HtmlTokenKind
        Text
        StartTag
        EndTag
    End Enum

    ''' <summary>One attribute, name lowercased.</summary>
    Public NotInheritable Class HtmlAttribute

        Public Sub New(name As String, value As String)
            Me.Name = name
            Me.Value = value
        End Sub

        Public ReadOnly Name As String
        Public ReadOnly Value As String
    End Class

    Public NotInheritable Class HtmlToken

        Public Property Kind As HtmlTokenKind
        Public Property Name As String = String.Empty
        Public Property Text As String = String.Empty
        Public Property Attributes As New List(Of HtmlAttribute)()
        Public Property SelfClosing As Boolean

    End Class

    ''' <summary>
    ''' One node of the document tree. A text node has TagName "#text" and its
    ''' content in Text; an element has attributes and children.
    ''' </summary>
    Public NotInheritable Class HtmlElement

        Public Property TagName As String = String.Empty
        Public Property Attributes As New Dictionary(Of String, String)()
        Public Property Children As New List(Of HtmlElement)()
        Public Property Text As String = String.Empty
        Public Property Parent As HtmlElement

        Public ReadOnly Property IsText As Boolean
            Get
                Return TagName = "#text"
            End Get
        End Property

        ''' <summary>Attribute value, or an empty string. Never Nothing.</summary>
        Public Function Attribute(name As String) As String
            If String.IsNullOrEmpty(name) Then Return String.Empty
            Dim foundValue As String = Nothing
            If Attributes.TryGetValue(name.ToLowerInvariant(), foundValue) Then
                Return If(foundValue, String.Empty)
            End If
            Return String.Empty
        End Function

        Public Overrides Function ToString() As String
            If IsText Then Return "#text"
            Return TagName
        End Function
    End Class

End Namespace
