# C#-ism to idiomatic F# rewrites

A before/after catalog for the most common ways C# habits leak into F#. Each pair compiles.

## 1. Loop + mutable accumulator to a collection function

C#-in-F#:

```fsharp
let sumOfSquares xs =
    let mutable total = 0
    for x in xs do
        total <- total + x * x
    total
```

Idiomatic:

```fsharp
let sumOfSquares xs = xs |> List.sumBy (fun x -> x * x)
```

## 2. Building a list by mutation to map/filter/choose

C#-in-F#:

```fsharp
let evensDoubled xs =
    let mutable result = []
    for x in xs do
        if x % 2 = 0 then
            result <- (x * 2) :: result
    List.rev result
```

Idiomatic:

```fsharp
let evensDoubled xs =
    xs
    |> List.filter (fun x -> x % 2 = 0)
    |> List.map (fun x -> x * 2)
```

When the test and the projection are one step, prefer `List.choose`:

```fsharp
let parsedValues xs =
    xs |> List.choose (fun s -> match System.Int32.TryParse s with
                                | true, v -> Some v
                                | _ -> None)
```

## 3. null to Option

C#-in-F#:

```fsharp
let displayName user =
    if user.Name <> null then user.Name.ToUpper()
    else "ANONYMOUS"
```

Idiomatic (model absence as `Option`, not `null`):

```fsharp
let displayName user =
    user.Name
    |> Option.map (fun n -> n.ToUpper())
    |> Option.defaultValue "ANONYMOUS"
```

For .NET APIs that really return null, convert at the boundary with `Option.ofObj`.

## 4. Statement-style if to an expression

C#-in-F#:

```fsharp
let classify n =
    let mutable label = ""
    if n < 0 then label <- "negative"
    elif n = 0 then label <- "zero"
    else label <- "positive"
    label
```

Idiomatic (the whole `if`/`elif`/`else` is one value):

```fsharp
let classify n =
    if n < 0 then "negative"
    elif n = 0 then "zero"
    else "positive"
```

## 5. Nested if / type-tests to match

C#-in-F#:

```fsharp
let describe shape =
    if shape.Kind = "circle" then sprintf "circle r=%f" shape.Radius
    elif shape.Kind = "rect" then sprintf "rect %fx%f" shape.W shape.H
    else "unknown"
```

Idiomatic (model the shape as a DU, then match - see `fsharp-domain-modeling`):

```fsharp
type Shape =
    | Circle of radius: float
    | Rect of width: float * height: float

let describe shape =
    match shape with
    | Circle r -> sprintf "circle r=%f" r
    | Rect (w, h) -> sprintf "rect %fx%f" w h
```

## 6. Nested calls to a pipeline

C#-in-F#:

```fsharp
let result = List.sum (List.map square (List.filter isPositive numbers))
```

Idiomatic:

```fsharp
let result =
    numbers
    |> List.filter isPositive
    |> List.map square
    |> List.sum
```

## 7. .Result / .Wait() to async/task

C#-in-F#:

```fsharp
let content = httpClient.GetStringAsync(url).Result
```

Idiomatic (do not block; stay in the async context - see `fsharp-async-and-tasks`):

```fsharp
let fetch url =
    task {
        let! content = httpClient.GetStringAsync(url)
        return content
    }
```

## 8. Manual recursion to fold

C#-in-F#:

```fsharp
let rec total xs =
    match xs with
    | [] -> 0
    | head :: tail -> head + total tail
```

Idiomatic when it is a straight reduction:

```fsharp
let total xs = xs |> List.fold (+) 0
```

(Hand-written recursion is still idiomatic when the traversal is not a simple fold.)

## 9. Class with mutable fields to a record

C#-in-F#:

```fsharp
type Point() =
    member val X = 0.0 with get, set
    member val Y = 0.0 with get, set
```

Idiomatic for plain data (immutable, structural equality, `with` for updates):

```fsharp
type Point = { X: float; Y: float }

let moved p dx dy = { p with X = p.X + dx; Y = p.Y + dy }
```

## 10. Exhaustive matching over a stray wildcard

Fragile - a new case is silently mishandled:

```fsharp
match status with
| Active -> "on"
| _ -> "off"
```

Robust - the compiler warns when `Status` gains a case:

```fsharp
match status with
| Active -> "on"
| Suspended -> "off"
| Closed -> "off"
```
