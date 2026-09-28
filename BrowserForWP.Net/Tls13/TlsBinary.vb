' BrowserForWP — big-endian TLS byte reader and writer.
'
' Transliteration of the Writer/Reader pair in tools/proto/tls13.mjs. TLS is
' entirely big-endian, and almost every handshake bug is an off-by-one in a
' length prefix, so the length-prefixed helpers are named for the size of the
' *length field* (vec8 = 1-byte length, vec16 = 2-byte length, vec24 = 3-byte)
' rather than for the payload.

Imports System.Collections.Generic
Imports System.Text

Namespace Tls13

    ''' <summary>Append-only big-endian writer for handshake messages.</summary>
    Public NotInheritable Class TlsWriter

        Private ReadOnly _parts As New List(Of Byte)()

        Public Function U8(value As Integer) As TlsWriter
            _parts.Add(CByte(value And &HFF))
            Return Me
        End Function

        Public Function U16(value As Integer) As TlsWriter
            _parts.Add(CByte((value >> 8) And &HFF))
            _parts.Add(CByte(value And &HFF))
            Return Me
        End Function

        Public Function U24(value As Integer) As TlsWriter
            _parts.Add(CByte((value >> 16) And &HFF))
            _parts.Add(CByte((value >> 8) And &HFF))
            _parts.Add(CByte(value And &HFF))
            Return Me
        End Function

        Public Function Bytes(data As Byte()) As TlsWriter
            If data IsNot Nothing AndAlso data.Length > 0 Then _parts.AddRange(data)
            Return Me
        End Function

        ''' <summary>Writes a 1-byte length followed by the payload.</summary>
        Public Function Vec8(data As Byte()) As TlsWriter
            Return U8(If(data Is Nothing, 0, data.Length)).Bytes(data)
        End Function

        ''' <summary>Writes a 2-byte length followed by the payload.</summary>
        Public Function Vec16(data As Byte()) As TlsWriter
            Return U16(If(data Is Nothing, 0, data.Length)).Bytes(data)
        End Function

        ''' <summary>Writes a 3-byte length followed by the payload.</summary>
        Public Function Vec24(data As Byte()) As TlsWriter
            Return U24(If(data Is Nothing, 0, data.Length)).Bytes(data)
        End Function

        ''' <summary>ASCII bytes, for host names and ALPN protocol names.</summary>
        Public Function Ascii(text As String) As TlsWriter
            Return Bytes(Encoding.UTF8.GetBytes(If(text, String.Empty)))
        End Function

        Public ReadOnly Property Length As Integer
            Get
                Return _parts.Count
            End Get
        End Property

        Public Function ToArray() As Byte()
            Return _parts.ToArray()
        End Function
    End Class

    ''' <summary>
    ''' A bounded big-endian cursor. Every read is length-checked and raises
    ''' <see cref="TlsProtocolException"/> rather than silently returning zeros,
    ''' because a truncated handshake message is a protocol failure, not a value.
    ''' </summary>
    Public NotInheritable Class TlsReader

        Private ReadOnly _data As Byte()
        Private _position As Integer

        Public Sub New(data As Byte())
            _data = If(data, New Byte() {})
        End Sub

        Public ReadOnly Property Remaining As Integer
            Get
                Return _data.Length - _position
            End Get
        End Property

        Private Sub Require(count As Integer)
            If Remaining < count Then
                Throw New TlsProtocolException(
                    "truncated message: needed " & count & " bytes, " & Remaining & " remain")
            End If
        End Sub

        Public Function ReadU8() As Integer
            Require(1)
            Return _data(_position)
        End Function

        Public Function ReadU16() As Integer
            Require(2)
            Dim value = (CInt(_data(_position)) << 8) Or CInt(_data(_position + 1))
            _position += 2
            Return value
        End Function

        Public Function ReadU24() As Integer
            Require(3)
            Dim value = (CInt(_data(_position)) << 16) Or
                        (CInt(_data(_position + 1)) << 8) Or
                        CInt(_data(_position + 2))
            _position += 3
            Return value
        End Function

        Public Function ReadBytes(count As Integer) As Byte()
            Require(count)
            Dim slice(count - 1) As Byte
            Array.Copy(_data, _position, slice, 0, count)
            _position += count
            Return slice
        End Function

        ''' <summary>Reads a 1-byte length then that many bytes.</summary>
        Public Function ReadVec8() As Byte()
            Return ReadBytes(ReadU8())
        End Function

        ''' <summary>Reads a 2-byte length then that many bytes.</summary>
        Public Function ReadVec16() As Byte()
            Return ReadBytes(ReadU16())
        End Function

        ''' <summary>Reads a 3-byte length then that many bytes.</summary>
        Public Function ReadVec24() As Byte()
            Return ReadBytes(ReadU24())
        End Function

        ''' <summary>Consumes the rest of the buffer.</summary>
        Public Function ReadRest() As Byte()
            Return ReadBytes(Remaining)
        End Function

        ''' <summary>
        ''' A sub-reader over the next length-prefixed block. Used for extension
        ''' lists and certificate lists, where the container is length-framed.
        ''' </summary>
        Public Function SubReaderVec16() As TlsReader
            Return New TlsReader(ReadVec16())
        End Function
    End Class

    ''' <summary>
    ''' Any deviation from RFC 8446. Raised instead of returning a partial result:
    ''' a TLS layer that guesses is worse than one that fails.
    ''' </summary>
    Public Class TlsProtocolException
        Inherits Exception

        Public Sub New(message As String)
            MyBase.New(message)
        End Sub

        Public Sub New(message As String, innerException As Exception)
            MyBase.New(message, innerException)
        End Sub
    End Class

End Namespace
