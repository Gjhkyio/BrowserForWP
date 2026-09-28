' BrowserForWP — TLS 1.3 running transcript hash.
'
' RFC 8446 §4.4.1. Every handshake message is fed, in order and unmodified, into
' a running hash. The same transcript is hashed at several distinct points:
'
'   * after ServerHello          -> the handshake traffic secrets
'   * after the server's Finished -> the application traffic secrets
'
' Because it is hashed more than once at different prefix lengths, the messages
' have to be buffered rather than hashed incrementally with a single
' HashAlgorithmProvider: GetValueAndReset would destroy the state. Handshake
' transcripts are a few kilobytes, so buffering is the right trade.
'
' Only Handshake-type records go in. ChangeCipherSpec is not a handshake
' message, alerts are not handshake messages, and the record framing itself is
' not hashed at all — a record boundary may fall anywhere, including mid-message.

Imports System.Collections.Generic
Imports BrowserForWP.Crypto

Namespace Tls13

    ''' <summary>The running transcript hash of a TLS 1.3 handshake.</summary>
    Public NotInheritable Class TranscriptHash

        Private ReadOnly _messages As New List(Of Byte)()

        ''' <summary>Appends one complete handshake message, including its 4-byte header.</summary>
        Public Sub Add(message As Byte())
            If message Is Nothing OrElse message.Length = 0 Then Return
            _messages.AddRange(message)
        End Sub

        ''' <summary>The SHA-256 of every message added so far.</summary>
        Public Function Compute() As Byte()
            Return Hkdf.Sha256(_messages.ToArray())
        End Function

        Public ReadOnly Property MessageCount As Integer
            Get
                Return _messages.Count
            End Get
        End Property

        Public Sub Clear()
            _messages.Clear()
        End Sub
    End Class

End Namespace
