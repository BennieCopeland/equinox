﻿module Foldunk.InMemoryStore.Integration.InMemoryStoreIntegration

open Backend
open Domain
open Foldunk
open Swensen.Unquote

let inline createInMemoryStore<'state,'event> () : Foldunk.IEventStream<'state,'event> =
    Foldunk.Stores.InMemoryStore.MemoryStreamStore() :> _

let createServiceWithInMemoryStore () =
    let store = createInMemoryStore()
    Carts.Service(fun _codec -> store)

#nowarn "1182" // From hereon in, we may have some 'unused' privates (the tests)

type Tests(testOutputHelper) =
    let testOutput = TestOutputAdapter testOutputHelper
    let createLog () = createLogger (testOutput.Subscribe >> ignore)

    [<AutoData>]
    let ``Basic tracer bullet, sending a command and verifying the folded result directly and via a reload``
            cartId1 cartId2 ((_,skuId,quantity) as args) = Async.RunSynchronously <| async {
        let log, service = createLog (), createServiceWithInMemoryStore ()
        let decide (ctx: DecisionContext<_,_>) = async {
            Cart.Commands.AddItem args |> Cart.Commands.interpret |> ctx.Execute
            return ctx.Complete ctx.State }

        // Act: Run the decision twice...
        let actTrappingStateAsSaved cartId =
            service.Run log cartId decide
        let actLoadingStateSeparately cartId = async {
            let! _ = service.Run log cartId decide
            return! service.Load log cartId }
        let! expected = cartId1 |> actTrappingStateAsSaved
        let! actual = cartId2 |> actLoadingStateSeparately

        // Assert 1. Despite being on different streams (and being in-memory vs re-loaded) we expect the same outcome
        test <@ expected = actual @>

        // Assert 2. Verify that the Command got correctly reflected in the state, with no extraneous effects
        let verifyFoldedStateReflectsCommand = function
            | { Cart.Folds.State.items = [ item ] } ->
                let expectedItem : Cart.Folds.ItemInfo = { skuId = skuId; quantity = quantity; returnsWaived = false }
                test <@ expectedItem = item @>
            | x -> x |> failwithf "Expected to find item, got %A"
        verifyFoldedStateReflectsCommand expected
        verifyFoldedStateReflectsCommand actual
    }