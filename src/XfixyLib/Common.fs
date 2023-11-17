namespace Xfixy.Common

type NoteKind =
    | Text
    | Error

type Note = { Note: string; Kind: NoteKind }

module Note =
    let mkNoteFromLine (line: string) =
        // examples:
        // theNote
        // #error theNote
        let isErrorKind = line.StartsWith("#error")

        let note =
            if isErrorKind then
                let theNote = line.Replace("#error ", "")

                { Kind = NoteKind.Error
                  Note = theNote }
            else
                { Kind = NoteKind.Text; Note = line }

        note
