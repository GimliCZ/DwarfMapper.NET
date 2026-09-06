// SPDX-License-Identifier: GPL-2.0-only

using CleanCorpus.Ordering;
using CleanCorpus.Ordering.Contracts;
using CleanCorpus.Ordering.Legacy;
using CleanCorpus.Ordering.Mapping;
using CleanCorpus.Ordering.Persistence;
using CleanCorpus.Ordering.Presentation;

namespace CleanCorpus
{
    /// <summary>
    ///     The ordering corpus, mapped and compared against the hand-written mapper.
    /// </summary>
    /// <remarks>
    ///     The other two corpora in this directory were each grown to pin a defect, so each is shaped like the
    ///     bug that birthed it. This one is shaped like an application: an EF Core model, an HTTP API, three
    ///     envelopes, a wide integration row, a half-migrated legacy corner and a presentation layer whose
    ///     targets are not transfer models. Nothing here needs a comment explaining why anyone would write it.
    /// </remarks>
    public class OrderingAgreementTests
    {
        private static readonly Guid CustomerKey = new("2a8f4d1c-9b76-4f01-9c3e-71b0d4c5e882");

        private static readonly Guid OrderKey = new("b31c07d5-4a2e-4c88-9f10-6e5a2d93b447");

        private static readonly DateTimeOffset Placed = new(2026, 4, 17, 10, 15, 0, TimeSpan.Zero);

        // ── Entity → API DTO, the dominant shape ────────────────────────────────────────────────────────────

        [Fact]
        public void An_entity_maps_to_its_response_exactly_as_the_hand_written_mapper_does()
        {
            var source = AnOrder();

            var expected = HandWritten.ToResponse(source);
            var actual = new OrderReadMappers().ToResponse(source);

            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.Reference, actual.Reference);
            Assert.Equal(expected.Status, actual.Status);
            Assert.Equal(expected.PlacedAt, actual.PlacedAt);
            Assert.Equal(expected.DispatchedAt, actual.DispatchedAt);
            Assert.Equal(expected.FreightCharge, actual.FreightCharge);
            Assert.Equal(expected.Lines.Count, actual.Lines.Count);
            Assert.Equal(expected.Lines[1].Sku, actual.Lines[1].Sku);
            Assert.Equal(expected.Lines[1].DiscountPercent, actual.Lines[1].DiscountPercent);
        }

        [Fact]
        public void The_navigation_property_is_flattened_two_and_three_levels_deep()
        {
            var source = AnOrder();

            var expected = HandWritten.ToResponse(source);
            var actual = new OrderReadMappers().ToResponse(source);

            Assert.Equal(expected.CustomerName, actual.CustomerName);
            Assert.Equal(expected.CustomerEmail, actual.CustomerEmail);
            Assert.Equal(expected.BillingTown, actual.BillingTown);
            Assert.Equal("Ashford", actual.BillingTown);
        }

        [Fact]
        public void The_concurrency_token_reaches_no_response()
        {
            // RowVersion belongs to the database. Nothing declares it ignored on the read side because the
            // response has no member for it — target completeness is what governs, and an unread SOURCE member
            // is not an error. Asserted so the silence is a checked property rather than an assumption.
            var names = typeof(OrderResponse).GetProperties().Select(p => p.Name).ToList();

            Assert.DoesNotContain(nameof(OrderEntity.RowVersion), names);
            Assert.NotEmpty(AnOrder().RowVersion);
        }

        // ── Request → entity → response, the three-hop shape ────────────────────────────────────────────────

        [Fact]
        public void The_write_path_carries_the_client_supplied_members_and_invents_nothing()
        {
            var request = APlaceOrderRequest();

            var expected = HandWritten.ToEntity(request);
            var actual = new OrderWriteMappers().ToEntity(request);

            Assert.Equal(expected.Reference, actual.Reference);
            Assert.Equal(expected.CustomerId, actual.CustomerId);
            Assert.Equal(expected.FreightCharge, actual.FreightCharge);
            Assert.Equal(expected.Lines.Count, actual.Lines.Count);
            Assert.Equal(expected.Lines[0].Sku, actual.Lines[0].Sku);

            // The six members the server owns. A request cannot set them, and the build says so.
            Assert.Equal(Guid.Empty, actual.Id);
            Assert.Equal(OrderStatus.Draft, actual.Status);
            Assert.Equal(default, actual.PlacedAt);
            Assert.Null(actual.DispatchedAt);
            Assert.Empty(actual.RowVersion);
            Assert.Equal(0, actual.Lines[0].Id);
        }

        [Fact]
        public void The_three_hops_agree_end_to_end()
        {
            // Request in, entity in the middle carrying what the database owns, response out. The middle type
            // is the only one that ever holds the key and the concurrency token.
            var entity = new OrderWriteMappers().ToEntity(APlaceOrderRequest());
            entity.Id = OrderKey;
            entity.Status = OrderStatus.Placed;
            entity.PlacedAt = Placed;
            entity.Customer = ACustomer();

            var response = new OrderReadMappers().ToResponse(entity);

            Assert.Equal(OrderKey, response.Id);
            Assert.Equal("ORD-40218", response.Reference);
            Assert.Equal(nameof(OrderStatus.Placed), response.Status);
            Assert.Equal(2, response.Lines.Count);
        }

        [Fact]
        public void A_constructor_that_validates_is_the_only_route_in()
        {
            var accepted = new PromotionMappers().ToEntities([
                new CreatePromotionRequest
                {
                    Code = "SPRING10",
                    PercentOff = 10m,
                    ExpiresAt = Placed.AddDays(30)
                }
            ]);

            Assert.Equal("SPRING10", Assert.Single(accepted).Code);
            Assert.Equal(0, accepted[0].Id);

            // And the invariant still runs: the mapper goes through the constructor rather than around it.
            Assert.Throws<ArgumentOutOfRangeException>(() => new PromotionMappers().ToEntities([
                new CreatePromotionRequest
                {
                    Code = "FREE",
                    PercentOff = 99m
                }
            ]));
        }

        // ── Envelope generics ───────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void A_single_payload_envelope_maps_from_one_wrapper_declaration()
        {
            var source = new Result<OrderEntity>
            {
                Value = AnOrder(),
                Error = null
            };

            var mapped = new OrderReadMappers().ToResult(source);

            Assert.Null(mapped.Error);
            Assert.Equal(HandWritten.ToResponse(source.Value).Reference, mapped.Value.Reference);
        }

        [Fact]
        public void A_second_envelope_over_the_same_payload_needs_no_second_payload_map()
        {
            var source = new ApiResponse<OrderEntity>
            {
                Data = AnOrder(),
                TraceId = "trace-8801",
                GeneratedAt = Placed
            };

            var mapped = new OrderReadMappers().ToEnvelope(source);

            Assert.Equal("trace-8801", mapped.TraceId);
            Assert.Equal(Placed, mapped.GeneratedAt);
            Assert.Equal(HandWritten.ToResponse(source.Data).CustomerName, mapped.Data.CustomerName);
        }

        [Fact]
        public void A_page_of_results_maps_its_metadata_and_its_items()
        {
            var source = new PagedResult<OrderEntity>
            {
                Items = [AnOrder()],
                Page = 3,
                PageSize = 25,
                TotalCount = 61
            };

            var mapped = new OrderReadMappers().Map(source);

            Assert.Equal(3, mapped.Page);
            Assert.Equal(25, mapped.PageSize);
            Assert.Equal(61, mapped.TotalCount);
            Assert.Equal("ORD-40218", Assert.Single(mapped.Items).Reference);
        }

        // ── Collections at every shape the engine supports ──────────────────────────────────────────────────

        [Fact]
        public void An_array_a_read_only_list_a_list_and_a_dictionary_map_on_one_response()
        {
            var source = ACustomer();
            source.Orders.Add(AnOrder());

            var expected = HandWritten.ToProfile(source);
            var actual = new CustomerProfileMappers().ToProfile(source);

            Assert.Equal(expected.Segments, actual.Segments);
            Assert.Equal(expected.Contacts.Count, actual.Contacts.Count);
            Assert.Equal(expected.Contacts[0].FullName, actual.Contacts[0].FullName);
            Assert.Equal(expected.RecentOrders.Count, actual.RecentOrders.Count);
            Assert.Equal(expected.RecentOrders[0].Reference, actual.RecentOrders[0].Reference);
            Assert.Equal(expected.Preferences, actual.Preferences);
            Assert.Equal("Ashford", actual.BillingTown);
        }

        [Fact]
        public void A_collection_copies_the_elements_rather_than_the_reference()
        {
            var source = ACustomer();

            var mapped = new CustomerProfileMappers().ToProfile(source);
            source.Contacts[0].FullName = "changed after mapping";

            Assert.Equal("H. Okonkwo", mapped.Contacts[0].FullName);
        }

        // ── The wide integration row ────────────────────────────────────────────────────────────────────────

        [Fact]
        public void A_thirty_member_integration_row_agrees_member_for_member()
        {
            var source = ASettlementRecord();

            var expected = HandWritten.ToRow(source);
            var actual = Assert.Single(new ReportingMappers().ToRows([source]));

            Assert.Equal(HandWritten.Describe(expected), HandWritten.Describe(actual));
            Assert.Equal(30, typeof(SettlementRowDto).GetProperties().Length);
        }

        // ── Modern C# targets ───────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void A_positional_record_target_is_constructed_by_parameter()
        {
            var summary = new CustomerProfileMappers().ToSummary(ACustomer());

            Assert.Equal(CustomerKey, summary.Id);
            Assert.Equal("Okonkwo Supplies", summary.DisplayName);
            Assert.Equal("orders@okonkwo.invalid", summary.EmailAddress);
        }

        [Fact]
        public void A_primary_constructor_class_is_constructed_by_parameter()
        {
            var totals = new ReportingMappers().ToTotals(new OrderTotalsRow
            {
                Net = 1200.00m,
                Tax = 240.00m,
                Gross = 1440.00m
            });

            Assert.Equal(1200.00m, totals.Net);
            Assert.Equal(240.00m, totals.Tax);
            Assert.Equal(1440.00m, totals.Gross);
        }

        [Fact]
        public void A_readonly_record_struct_element_lives_inside_its_list()
        {
            var lines = new ReportingMappers().ToMoneyLines(AnOrder().Lines);

            Assert.True(typeof(MoneyLine).IsValueType);
            Assert.Equal(2, lines.Count);
            Assert.Equal("AXE-1", lines[0].Sku);
            Assert.Equal(24.50m, lines[0].UnitPrice);
        }

        [Fact]
        public void A_response_where_everything_is_optional_keeps_every_absence()
        {
            var known = new CustomerProfileMappers().ToEnrichment(new CustomerEnrichmentRecord
            {
                Id = CustomerKey,
                LegalName = "Okonkwo Supplies Limited",
                EmployeeCount = 41,
                IncorporatedOn = new DateOnly(2011, 6, 2)
            });

            Assert.Equal("Okonkwo Supplies Limited", known.LegalName);
            Assert.Equal(41, known.EmployeeCount);
            Assert.Equal(new DateOnly(2011, 6, 2), known.IncorporatedOn);
            Assert.Null(known.TradingName);
            Assert.Null(known.VatNumber);
            Assert.Null(known.AnnualTurnover);
            Assert.Null(known.LastVerifiedAt);
            Assert.Null(known.IsPubliclyListed);
        }

        // ── The un-annotated corner ─────────────────────────────────────────────────────────────────────────

        [Fact]
        public void An_oblivious_record_maps_into_annotated_contracts()
        {
            var source = ALegacyRecord();

            var expected = HandWritten.ToView(source);
            var actual = new LegacyMappers().ToView(source);

            Assert.Equal(expected.CustomerNumber, actual.CustomerNumber);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.Email, actual.Email);
            Assert.Equal(expected.LastOrderedOn, actual.LastOrderedOn);
            Assert.Equal(expected.Lines.Count, actual.Lines.Count);
            Assert.Equal(expected.Lines[0].ProductCode, actual.Lines[0].ProductCode);
        }

        [Fact]
        public void And_back_across_the_same_boundary()
        {
            var view = new LegacyMappers().ToView(ALegacyRecord());

            var record = new LegacyMappers().ToRecord(view);

            Assert.Equal("CU-00418", record.CustomerNumber);
            Assert.Equal("Okonkwo Supplies", record.Name);
            Assert.Single(record.Lines);
            Assert.Equal(3, record.Lines[0].Quantity);
        }

        // ── Types the struct advice must not reach, mapped anyway ───────────────────────────────────────────

        [Fact]
        public void A_polymorphic_step_keeps_the_member_only_its_own_kind_has()
        {
            List<DeliveryStepEntity> timeline =
            [
                new PickupStepEntity
                {
                    Id = 1,
                    OccurredAt = Placed,
                    Location = "Ashford depot",
                    CourierName = "N. Hartley"
                },
                new HandoverStepEntity
                {
                    Id = 2,
                    OccurredAt = Placed.AddHours(6),
                    Location = "12 Mill Lane",
                    SignedBy = "R. Aldous"
                }
            ];

            var views = new DeliveryMappers().ToTimeline(timeline);

            Assert.Equal("N. Hartley", Assert.IsType<PickupStepView>(views[0]).CourierName);
            Assert.Equal("R. Aldous", Assert.IsType<HandoverStepView>(views[1]).SignedBy);
            Assert.Equal("Ashford depot", views[0].Location);
        }

        [Fact]
        public void A_view_model_that_raises_change_notifications_is_mapped_and_still_raises_them()
        {
            var rows = new PresentationMappers().ToRows([HandWritten.ToResponse(AnOrder())]);

            var row = Assert.Single(rows);
            Assert.Equal(OrderKey, row.Id);
            Assert.Equal(nameof(OrderStatus.Placed), row.Status);

            var raised = 0;
            row.PropertyChanged += (_, _) => raised++;
            row.Status = nameof(OrderStatus.Dispatched);
            row.Status = nameof(OrderStatus.Dispatched);

            Assert.Equal(1, raised);
        }

        [Fact]
        public void A_target_that_owns_a_lifetime_is_mapped_and_still_disposes()
        {
            var queue = new PresentationMappers().ToOutbound([
                new EmailNotificationRequest
                {
                    To = "orders@okonkwo.invalid",
                    Subject = "Order ORD-40218 dispatched",
                    Body = "Two items are on their way."
                }
            ]);

            var message = Assert.Single(queue);
            Assert.Equal("orders@okonkwo.invalid", message.To);
            Assert.Empty(message.Attachments);

            message.Attach([1, 2, 3]);
            Assert.Single(message.Attachments);

            message.Dispose();
            Assert.Empty(message.Attachments);
        }

        [Fact]
        public void A_computed_member_is_not_assigned_and_computes_from_what_was()
        {
            var views = new PresentationMappers().ToInvoiceLines(AnOrder().Lines);

            Assert.Equal(2, views.Count);
            Assert.Equal(49.00m, views[0].LineTotal);
            Assert.Null(typeof(InvoiceLineView).GetProperty(nameof(InvoiceLineView.LineTotal))!.SetMethod);
        }

        // ── Fixtures ────────────────────────────────────────────────────────────────────────────────────────

        private static OrderEntity AnOrder()
        {
            return new OrderEntity
            {
                Id = OrderKey,
                Reference = "ORD-40218",
                CustomerId = CustomerKey,
                Customer = ACustomer(),
                Status = OrderStatus.Placed,
                PlacedAt = Placed,
                DispatchedAt = null,
                FreightCharge = 4.99m,
                RowVersion = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x27, 0x0F],
                Lines =
                [
                    new OrderLineEntity
                    {
                        Id = 1,
                        OrderId = OrderKey,
                        Sku = "AXE-1",
                        Description = "Forged hand axe",
                        Quantity = 2,
                        UnitPrice = 24.50m,
                        DiscountPercent = 0m
                    },
                    new OrderLineEntity
                    {
                        Id = 2,
                        OrderId = OrderKey,
                        Sku = "ALE-9",
                        Description = "Stout, case of six",
                        Quantity = 1,
                        UnitPrice = 18.00m,
                        DiscountPercent = 5m
                    }
                ]
            };
        }

        private static CustomerEntity ACustomer()
        {
            return new CustomerEntity
            {
                Id = CustomerKey,
                DisplayName = "Okonkwo Supplies",
                EmailAddress = "orders@okonkwo.invalid",
                TelephoneNumber = null,
                RegisteredOn = new DateOnly(2023, 11, 2),
                BillingAddress = new PostalAddressEntity
                {
                    Line1 = "12 Mill Lane",
                    Line2 = null,
                    Town = "Ashford",
                    Postcode = "AS1 4QD",
                    CountryCode = "GB"
                },
                Segments = ["trade", "priority"],
                Preferences = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["invoice-format"] = "pdf",
                    ["locale"] = "en-GB"
                },
                Contacts =
                [
                    new ContactEntity
                    {
                        Id = 7,
                        CustomerId = CustomerKey,
                        Role = "accounts",
                        FullName = "H. Okonkwo",
                        EmailAddress = "h@okonkwo.invalid"
                    }
                ]
            };
        }

        private static PlaceOrderRequest APlaceOrderRequest()
        {
            return new PlaceOrderRequest
            {
                Reference = "ORD-40218",
                CustomerId = CustomerKey,
                FreightCharge = 4.99m,
                Lines =
                [
                    new OrderLineRequest
                    {
                        Sku = "AXE-1",
                        Description = "Forged hand axe",
                        Quantity = 2,
                        UnitPrice = 24.50m,
                        DiscountPercent = 0m
                    },
                    new OrderLineRequest
                    {
                        Sku = "ALE-9",
                        Description = "Stout, case of six",
                        Quantity = 1,
                        UnitPrice = 18.00m,
                        DiscountPercent = 5m
                    }
                ]
            };
        }

        private static SettlementRecord ASettlementRecord()
        {
            return new SettlementRecord
            {
                MerchantId = "M-90412",
                SettlementBatchId = "B-2026-04-18-01",
                TransactionId = "T-88301774",
                AuthorisationCode = "047AF2",
                CardScheme = "Visa",
                MaskedPan = "411111******1111",
                CardCountryCode = "GB",
                TerminalId = "TRM-14",
                StoreCode = "ASH-01",
                OrderReference = "ORD-40218",
                GrossAmount = 67.99m,
                SchemeFee = 0.11m,
                InterchangeFee = 0.42m,
                AcquirerFee = 0.19m,
                NetAmount = 67.27m,
                CurrencyCode = "GBP",
                SettlementCurrencyCode = "GBP",
                ExchangeRate = 1.0m,
                CapturedAt = Placed,
                SettledOn = new DateOnly(2026, 4, 18),
                ValueDate = new DateOnly(2026, 4, 20),
                IsRefund = false,
                IsChargeback = false,
                ChargebackReason = null,
                RefundOfTransactionId = null,
                InstalmentCount = 1,
                InstalmentNumber = 1,
                PayoutBatchReference = "P-2026-04-20-GBP",
                PayoutStatus = PayoutStatus.Paid,
                ProcessorMessage = null
            };
        }

        private static LegacyCustomerRecord ALegacyRecord()
        {
            return new LegacyCustomerRecord
            {
                CustomerNumber = "CU-00418",
                Name = "Okonkwo Supplies",
                Email = "orders@okonkwo.invalid",
                Phone = null,
                LastOrderedOn = new DateTime(2026, 4, 17, 10, 15, 0, DateTimeKind.Utc),
                Lines =
                [
                    new LegacyLineRecord
                    {
                        ProductCode = "AXE-1",
                        Quantity = 3,
                        LinePrice = 73.50m
                    }
                ]
            };
        }
    }
}
