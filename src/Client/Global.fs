module Global

type Page =
  | Home
  | Counter
  | About

let toHash page =
  match page with
  | About -> "#about"
  | Counter -> "#counter"
  | Home -> "#home"
