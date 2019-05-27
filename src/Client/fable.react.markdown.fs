module rec Fable.ReactMarkdownImport

open Fable.Core
open Fable.Core.JsInterop
open Fable.React

type ReactMarkdownProps =
//    | ClassName of string option
    | Source of string
    | SourcePos of bool option
    | EscapeHtml of bool option
    | SkipHtml of bool option
    | AllowedTypes of NodeType[] option
    | DisallowedTypes of NodeType[] option
    // TODO
    // abstract allowNode: (AllowNode -> float -> NodeType -> bool) option
    // abstract transformLinkUri: (string -> ReactNode -> string -> string) option
    // abstract transformImageUri: (string -> ReactNode -> string -> string -> string) option
    | UnwrapDisallowed of bool option

    // TODO
    // abstract renderers: obj option
(*
type [<AllowNullLiteral>] AllowNode =
    abstract ``type``: string
    abstract value: string option
    abstract depth: float option
    abstract children: ResizeArray<ReactNode> option

type [<AllowNullLiteral>] SourcePosition =
    abstract line: float
    abstract column: float
    abstract offset: float

type [<AllowNullLiteral>] NodePosition =
    abstract start: SourcePosition
    abstract ``end``: SourcePosition
    abstract indent: ResizeArray<float>
*)

type [<StringEnum>] [<RequireQualifiedAccess>] NodeType =
    | Root
    | Break
    | Paragraph
    | Emphasis
    | Strong
    | ThematicBreak
    | Blockquote
    | Delete
    | Link
    | Image
    | LinkReference
    | ImageReference
    | Table
    | TableHead
    | TableBody
    | TableRow
    | TableCell
    | List
    | ListItem
    | Definition
    | Heading
    | InlineCode
    | Code
    | Html
    | VirtualHtml

let inline reactMarkdown (props : ReactMarkdownProps list) =
    ofImport "default" "react-markdown" (keyValueList CaseRules.LowerFirst props) []
