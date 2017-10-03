module Views

open Suave.Html

let pageLayout pageTitle content =
    html [] [
        head [] [
            title [] pageTitle
        ]
        body [] content
    ]
let channels chanlist =
    [
    tag "ul" [] [
        for ch in chanlist do
            yield tag "li" [] (text ch)
    ]]