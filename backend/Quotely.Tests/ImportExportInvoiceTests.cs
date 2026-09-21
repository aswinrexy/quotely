using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Quotely.Api.DTOs;
using Quotely.Api.Models;
using Xunit;

namespace Quotely.Tests;

/// <summary>
/// Import/export invoices end to end, through the real HTTP pipeline.
///
/// The thread running through these: a trade document is an ordinary Invoice with extra detail,
/// so the things that must be proved are (a) the extra detail is correct, and (b) nothing that
/// already worked has been given a second implementation here.
/// </summary>
public class ImportExportInvoiceTests : IClassFixture<QuotelyApiFactory>
{
    private readonly QuotelyApiFactory _factory;

    public ImportExportInvoiceTests(QuotelyApiFactory factory) => _factory = factory;

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    // ---- fixtures -------------------------------------------------------

    private static async Task<CustomerDto> CreateCustomerAsync(HttpClient client, string name = "Root Express Trading")
    {
        var response = await client.PostAsJsonAsync("/api/customers", new
        {
            name,
            companyName = $"{name} LLC",
            email = "buyer@example.com",
            phone = "+971 50 000 0000",
            addressLine = "Building 10, Office M-12",
            city = "Dubai",
            state = "Dubai",
            postalCode = "294365",
            country = "UAE",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CustomerDto>())!;
    }

    /// <summary>
    /// The two goods lines from the reference proforma, with its real figures. Sample data in a
    /// test, never in a seed: the exporter and consignee on that document are a real business.
    /// </summary>
    private static object[] ReferenceLines => new object[]
    {
        new
        {
            marksAndNumbers = "1-",
            description = "BITTER GOURD",
            dimension = "42X32X13",
            hsCode = "07099330",
            netWeight = 435.60m,
            grossWeight = 501.60m,
            quantity = 132m,
            quantityUnit = "BOXES",
            rate = 590.00m,
            rateLabel = "Rate/Kg C&F INR",
        },
        new
        {
            marksAndNumbers = "2",
            description = "LADY FINGER",
            dimension = "42X32X13",
            hsCode = "07099930",
            netWeight = 435.60m,
            grossWeight = 501.60m,
            quantity = 132m,
            quantityUnit = "BOXES",
            rate = 550.00m,
            rateLabel = "Rate/Kg C&F INR",
        },
    };

    private static object Payload(Guid customerId, object[]? items = null, string tradeType = "Export",
        string documentType = "ProformaInvoice") => new
    {
        customerId,
        tradeType,
        documentType,
        invoiceDate = Today.ToString("yyyy-MM-dd"),
        documentNumber = "NAFPCL/103/26-27",
        currency = "INR",
        portOfLoading = "VARANASI AIRPORT",
        portOfDischarge = "SHARJAH",
        finalDestination = "SHARJAH",
        countryOfOrigin = "INDIA",
        countryOfFinalDestination = "UAE",
        preCarriageBy = "N/A",
        vesselOrFlightNumber = "BY AIR",
        termsOfDelivery = "T/T",
        termsOfPayment = "T/T",
        pricingTerm = "C&F",
        buyerSameAsConsignee = true,
        notifyPartyName = "PROVA FOODSTUFF TRADING L.L.C",
        notifyPartyAddress = "Block No. 3, Shop No. 26, Dubai - U.A.E.",
        items = items ?? ReferenceLines,
    };

    private static async Task<TradeInvoiceDto> CreateAsync(HttpClient client, Guid customerId, object? payload = null)
    {
        var response = await client.PostAsJsonAsync(
            "/api/import-export/invoices", payload ?? Payload(customerId));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TradeInvoiceDto>())!;
    }

    // =====================================================================
    // The arithmetic the reference document depends on
    // =====================================================================

    /// <summary>
    /// The single most important test in this file.
    ///
    /// The reference prints its rate column as "Rate/Kg" and multiplies by the PACKAGE COUNT:
    /// 132 boxes at 590 gives 77,880. Multiplying by the net weight — which the column heading
    /// plainly invites — gives 257,004, and the document would be wrong by more than triple with
    /// nothing on the page to show it.
    /// </summary>
    [Fact]
    public async Task The_reference_documents_totals_are_reproduced_exactly()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);

        var invoice = await CreateAsync(client, customer.Id);

        invoice.Items.Should().HaveCount(2);
        invoice.Items[0].LineTotal.Should().Be(77_880.00m);
        invoice.Items[1].LineTotal.Should().Be(72_600.00m);

        invoice.GrandTotal.Should().Be(150_480.00m);
        invoice.TotalNetWeight.Should().Be(871.20m);
        invoice.TotalGrossWeight.Should().Be(1003.20m);
        invoice.TotalPackages.Should().Be(264m);

        invoice.AmountInWords.Should().Be("INR One Lakh Fifty Thousand Four Hundred Eighty Only");
    }

    [Fact]
    public async Task A_line_priced_by_weight_multiplies_by_the_weight_instead()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);

        var invoice = await CreateAsync(client, customer.Id, Payload(customer.Id, new object[]
        {
            new
            {
                description = "BITTER GOURD",
                netWeight = 435.60m,
                grossWeight = 501.60m,
                quantity = 132m,
                quantityUnit = "BOXES",
                rate = 590.00m,
                rateBasis = "PerNetWeight",
            },
        }));

        // 435.60 kg at 590 — the figure the "Rate/Kg" heading implies, for the exporters who
        // genuinely do price that way.
        invoice.Items.Single().LineTotal.Should().Be(257_004.00m);
        invoice.GrandTotal.Should().Be(257_004.00m);
    }

    [Fact]
    public async Task Pricing_by_weight_without_a_weight_is_refused_rather_than_totalling_zero()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);

        var response = await client.PostAsJsonAsync("/api/import-export/invoices",
            Payload(customer.Id, new object[]
            {
                new { description = "BITTER GOURD", quantity = 10m, rate = 500m, rateBasis = "PerNetWeight" },
            }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("net weight");
    }

    [Fact]
    public async Task Gross_weight_below_net_weight_is_refused()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);

        var response = await client.PostAsJsonAsync("/api/import-export/invoices",
            Payload(customer.Id, new object[]
            {
                new { description = "BITTER GOURD", quantity = 10m, rate = 500m, netWeight = 100m, grossWeight = 90m },
            }));

        // Arithmetic, not regulation: packaging cannot weigh less than nothing, and whoever
        // receives the consignment will query it.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("cannot be less than its net weight");
    }

    [Fact]
    public async Task The_server_recomputes_totals_rather_than_trusting_the_client()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);

        // lineTotal and grandTotal are not part of the request contract at all — this proves a
        // caller who invents them cannot move the document's money.
        var response = await client.PostAsJsonAsync("/api/import-export/invoices", new
        {
            customerId = customer.Id,
            invoiceDate = Today.ToString("yyyy-MM-dd"),
            grandTotal = 1m,
            items = new object[]
            {
                new { description = "BITTER GOURD", quantity = 132m, rate = 590m, lineTotal = 1m },
            },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var invoice = (await response.Content.ReadFromJsonAsync<TradeInvoiceDto>())!;
        invoice.GrandTotal.Should().Be(77_880.00m);
    }

    // =====================================================================
    // The document itself
    // =====================================================================

    [Fact]
    public async Task An_export_document_keeps_its_shipment_and_regulatory_detail()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);

        var invoice = await CreateAsync(client, customer.Id);

        invoice.TradeType.Should().Be("Export");
        invoice.DocumentType.Should().Be("ProformaInvoice");
        invoice.DocumentNumber.Should().Be("NAFPCL/103/26-27");
        invoice.PortOfLoading.Should().Be("VARANASI AIRPORT");
        invoice.PortOfDischarge.Should().Be("SHARJAH");
        invoice.CountryOfOrigin.Should().Be("INDIA");
        invoice.CountryOfFinalDestination.Should().Be("UAE");
        invoice.NotifyPartyName.Should().Be("PROVA FOODSTUFF TRADING L.L.C");

        var line = invoice.Items[0];
        line.HsCode.Should().Be("07099330");
        line.Dimension.Should().Be("42X32X13");
        line.MarksAndNumbers.Should().Be("1-");
        line.QuantityUnit.Should().Be("BOXES");
    }

    [Fact]
    public async Task The_consignee_defaults_to_the_selected_customer()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client, "Gulf Fresh Foods");

        var invoice = await CreateAsync(client, customer.Id);

        // Picking a customer is meant to fill the consignee block in, not merely file the document.
        invoice.ConsigneeName.Should().Be("Gulf Fresh Foods");
        invoice.ConsigneeAddress.Should().Contain("Dubai");
    }

    [Fact]
    public async Task Buyer_details_are_cleared_when_the_buyer_is_the_consignee()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);

        var response = await client.PostAsJsonAsync("/api/import-export/invoices", new
        {
            customerId = customer.Id,
            invoiceDate = Today.ToString("yyyy-MM-dd"),
            buyerSameAsConsignee = true,
            buyerName = "SOMEONE ELSE ENTIRELY",
            buyerAddress = "Nowhere",
            items = ReferenceLines,
        });

        var invoice = (await response.Content.ReadFromJsonAsync<TradeInvoiceDto>())!;

        // Not merely ignored on the way out — cleared on the way in, so that unticking the box on
        // a later edit cannot resurrect a stale buyer nobody meant to name.
        invoice.BuyerSameAsConsignee.Should().BeTrue();
        invoice.BuyerName.Should().BeNull();
        invoice.BuyerAddress.Should().BeNull();
    }

    [Fact]
    public async Task An_import_document_is_the_same_document_with_the_parties_reversed()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client, "Shandong Produce Co");

        var invoice = await CreateAsync(client, customer.Id,
            Payload(customer.Id, tradeType: "Import"));

        invoice.TradeType.Should().Be("Import");
        // Same storage, same totals — only the printed labels differ, which is a PDF concern.
        invoice.GrandTotal.Should().Be(150_480.00m);
    }

    // =====================================================================
    // Payability — the reason proforma and commercial are different things
    // =====================================================================

    [Fact]
    public async Task A_proforma_invoice_is_never_payable()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);

        var invoice = await CreateAsync(client, customer.Id);

        // A proforma describes a shipment that has not happened. Offering a Pay button would
        // invite a customer to pay against a document their own bank will not recognise.
        invoice.IsPayable.Should().BeFalse();
    }

    [Fact]
    public async Task A_commercial_invoice_is_payable_like_any_other()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);

        var invoice = await CreateAsync(client, customer.Id,
            Payload(customer.Id, documentType: "CommercialInvoice"));

        invoice.IsPayable.Should().BeTrue();
    }

    // =====================================================================
    // Tenant isolation
    // =====================================================================

    [Fact]
    public async Task Another_businesss_trade_document_is_a_404()
    {
        var owner = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(owner);
        var invoice = await CreateAsync(owner, customer.Id);

        var stranger = await _factory.CreateSignedInClientAsync(connectPayments: false);

        // 404 rather than 403, exactly as everywhere else in Quotely, so ids cannot be probed.
        (await stranger.GetAsync($"/api/import-export/invoices/{invoice.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.DeleteAsync($"/api/import-export/invoices/{invoice.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.GetAsync($"/api/import-export/invoices/{invoice.Id}/pdf"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Another_businesss_customer_cannot_be_named_as_consignee()
    {
        var owner = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var theirCustomer = await CreateCustomerAsync(owner);

        var stranger = await _factory.CreateSignedInClientAsync(connectPayments: false);

        var response = await stranger.PostAsJsonAsync(
            "/api/import-export/invoices", Payload(theirCustomer.Id));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_list_never_contains_another_businesss_documents()
    {
        var owner = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(owner);
        await CreateAsync(owner, customer.Id);

        var stranger = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var page = await stranger.GetFromJsonAsync<PagedResult<TradeInvoiceListItemDto>>(
            "/api/import-export/invoices", QuotelyApiFactory.Json);

        page!.Items.Should().BeEmpty();
    }

    // =====================================================================
    // Not a side door into ordinary invoices
    // =====================================================================

    [Fact]
    public async Task A_standard_invoice_cannot_be_reached_through_the_trade_routes()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);

        var standard = await client.PostAsJsonAsync("/api/invoices", new
        {
            customerId = customer.Id,
            invoiceDate = Today.ToString("yyyy-MM-dd"),
            items = new[]
            {
                new { name = "Consulting", unit = "Service", quantity = 1, unitPrice = 1000, discount = 0, taxRate = 18 },
            },
        });
        standard.StatusCode.Should().Be(HttpStatusCode.Created);
        var standardInvoice = (await standard.Content.ReadFromJsonAsync<InvoiceDto>())!;

        // It is the caller's own invoice, and still a 404 here. A trade form has no idea about a
        // domestic invoice's tax lines, and editing one through it would quietly destroy them.
        (await client.GetAsync($"/api/import-export/invoices/{standardInvoice.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Trade_documents_do_not_appear_in_the_ordinary_invoice_list()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);
        var trade = await CreateAsync(client, customer.Id);

        var page = await client.GetFromJsonAsync<PagedResult<InvoiceListItemDto>>(
            "/api/invoices", QuotelyApiFactory.Json);

        // They share the entity and the numbering, not the list. Someone scanning their invoices
        // for what a domestic customer owes should not be reading proforma offers.
        page!.Items.Should().NotContain(i => i.Id == trade.Id);
    }

    [Fact]
    public async Task Both_kinds_of_invoice_draw_from_one_number_sequence()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);

        var standard = await client.PostAsJsonAsync("/api/invoices", new
        {
            customerId = customer.Id,
            invoiceDate = Today.ToString("yyyy-MM-dd"),
            items = new[]
            {
                new { name = "Consulting", unit = "Service", quantity = 1, unitPrice = 1000, discount = 0, taxRate = 0 },
            },
        });
        var first = (await standard.Content.ReadFromJsonAsync<InvoiceDto>())!;

        var trade = await CreateAsync(client, customer.Id);

        // Two documents in the same books must never carry the same number, which is the entire
        // reason a trade invoice does not get a counter of its own.
        trade.InvoiceNumber.Should().NotBe(first.InvoiceNumber);
        trade.InvoiceNumber.Should().Be("INV-000002");
    }

    // =====================================================================
    // Editing and deleting
    // =====================================================================

    [Fact]
    public async Task An_update_rewrites_the_goods_and_recomputes_every_total()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);
        var invoice = await CreateAsync(client, customer.Id);

        var response = await client.PutAsJsonAsync(
            $"/api/import-export/invoices/{invoice.Id}",
            Payload(customer.Id, new object[]
            {
                new
                {
                    description = "GREEN CHILLI",
                    hsCode = "07096010",
                    netWeight = 200m,
                    grossWeight = 240m,
                    quantity = 50m,
                    quantityUnit = "BAGS",
                    rate = 400m,
                },
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<TradeInvoiceDto>())!;

        updated.Items.Should().HaveCount(1);
        updated.GrandTotal.Should().Be(20_000.00m);
        updated.TotalNetWeight.Should().Be(200m);
        updated.TotalGrossWeight.Should().Be(240m);
        updated.TotalPackages.Should().Be(50m);
        updated.AmountInWords.Should().Be("INR Twenty Thousand Only");
    }

    [Fact]
    public async Task A_draft_can_be_deleted_and_an_issued_document_cannot()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);
        var invoice = await CreateAsync(client, customer.Id);

        var issued = await CreateAsync(client, customer.Id);
        await client.PutAsJsonAsync($"/api/import-export/invoices/{issued.Id}",
            new
            {
                customerId = customer.Id,
                invoiceDate = Today.ToString("yyyy-MM-dd"),
                status = "Sent",
                items = ReferenceLines,
            });

        (await client.DeleteAsync($"/api/import-export/invoices/{invoice.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.DeleteAsync($"/api/import-export/invoices/{issued.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // =====================================================================
    // Validation
    // =====================================================================

    [Fact]
    public async Task A_document_needs_at_least_one_line_of_goods()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);

        var response = await client.PostAsJsonAsync("/api/import-export/invoices", new
        {
            customerId = customer.Id,
            invoiceDate = Today.ToString("yyyy-MM-dd"),
            items = Array.Empty<object>(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_blank_country_of_origin_is_accepted_rather_than_policed()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);

        var response = await client.PostAsJsonAsync("/api/import-export/invoices", new
        {
            customerId = customer.Id,
            invoiceDate = Today.ToString("yyyy-MM-dd"),
            items = ReferenceLines,
        });

        // Quotely is a document builder, not a customs validator. It does not know which fields a
        // given consignment, country or commodity requires, and refusing to save over a blank one
        // would be claiming an authority it does not have. The form prompts; the server allows.
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task An_unknown_trade_type_is_rejected_clearly()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);

        var response = await client.PostAsJsonAsync("/api/import-export/invoices", new
        {
            customerId = customer.Id,
            tradeType = "Smuggling",
            invoiceDate = Today.ToString("yyyy-MM-dd"),
            items = ReferenceLines,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Export or Import");
    }

    // =====================================================================
    // PDF
    // =====================================================================

    [Fact]
    public async Task The_pdf_renders_and_is_named_after_the_exporters_own_number()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);
        var invoice = await CreateAsync(client, customer.Id);

        var response = await client.GetAsync($"/api/import-export/invoices/{invoice.Id}/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Should().StartWith("%PDF"u8.ToArray());
        // A real multi-block document, not an empty page that happened to render.
        bytes.Length.Should().BeGreaterThan(3000);

        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                       ?? response.Content.Headers.ContentDisposition?.FileName ?? string.Empty;
        fileName.Should().StartWith("PI-");
    }

    [Fact]
    public async Task A_commercial_invoice_pdf_is_named_differently_from_a_proforma()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);
        var invoice = await CreateAsync(client, customer.Id,
            Payload(customer.Id, documentType: "CommercialInvoice"));

        var response = await client.GetAsync($"/api/import-export/invoices/{invoice.Id}/pdf");
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                       ?? response.Content.Headers.ContentDisposition?.FileName ?? string.Empty;

        fileName.Should().StartWith("CI-");
    }

    [Fact]
    public async Task The_pdf_survives_long_text_many_lines_and_missing_optional_fields()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);

        // Forty lines forces pagination and a repeated header; the long description and the
        // absent optional fields are what a real form produces on a bad day.
        var lines = Enumerable.Range(1, 40).Select(i => (object)new
        {
            description = $"ORGANIC PRODUCE CONSIGNMENT LINE {i} WITH A DELIBERATELY LONG DESCRIPTION "
                          + "THAT WILL WRAP ACROSS SEVERAL LINES INSIDE ITS COLUMN",
            quantity = 10m + i,
            rate = 123.45m,
            quantityUnit = "BOXES",
        }).ToArray();

        var invoice = await CreateAsync(client, customer.Id, new
        {
            customerId = customer.Id,
            invoiceDate = Today.ToString("yyyy-MM-dd"),
            headerDeclarations = string.Join("\n", Enumerable.Repeat(
                "A DECLARATION LINE THAT IS ITSELF RATHER LONG AND HAS TO WRAP CLEANLY", 3)),
            footerDeclaration = string.Join(" ", Enumerable.Repeat(
                "We declare that this invoice shows the actual price of the goods described.", 6)),
            items = lines,
        });

        var response = await client.GetAsync($"/api/import-export/invoices/{invoice.Id}/pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).Length.Should().BeGreaterThan(5000);
    }

    [Fact]
    public async Task A_document_in_another_currency_renders_and_reads_in_that_currency()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);

        var invoice = await CreateAsync(client, customer.Id, new
        {
            customerId = customer.Id,
            invoiceDate = Today.ToString("yyyy-MM-dd"),
            currency = "AED",
            items = new object[] { new { description = "DATES", quantity = 100m, rate = 12.5m } },
        });

        invoice.Currency.Should().Be("AED");
        invoice.AmountInWords.Should().Be("AED One Thousand Two Hundred Fifty Only");

        (await client.GetAsync($"/api/import-export/invoices/{invoice.Id}/pdf"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // =====================================================================
    // Sharing reuses the existing mechanism
    // =====================================================================

    [Fact]
    public async Task A_trade_document_shares_through_the_same_public_link_machinery()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);
        var invoice = await CreateAsync(client, customer.Id);

        var response = await client.PostAsync(
            $"/api/import-export/invoices/{invoice.Id}/public-link", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var link = (await response.Content.ReadFromJsonAsync<PublicInvoiceLinkDto>())!;
        link.Url.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_standard_invoice_cannot_be_linked_through_the_trade_route()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);

        var standard = await client.PostAsJsonAsync("/api/invoices", new
        {
            customerId = customer.Id,
            invoiceDate = Today.ToString("yyyy-MM-dd"),
            items = new[]
            {
                new { name = "Consulting", unit = "Service", quantity = 1, unitPrice = 1000, discount = 0, taxRate = 0 },
            },
        });
        var standardInvoice = (await standard.Content.ReadFromJsonAsync<InvoiceDto>())!;

        (await client.PostAsync($"/api/import-export/invoices/{standardInvoice.Id}/public-link", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // =====================================================================
    // Trade profile defaults
    // =====================================================================

    [Fact]
    public async Task Saved_trade_settings_fill_a_new_document_in()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);

        await client.PutAsJsonAsync("/api/import-export/profile", new
        {
            iecNumber = "AAICN4546R",
            gstNumber = "09AAICN4546R1ZL",
            panNumber = "AAICN4546R",
            apedaRegistrationNumber = "01780",
            defaultCountryOfOrigin = "INDIA",
            defaultTermsOfPayment = "T/T",
            defaultPortOfLoading = "VARANASI AIRPORT",
            defaultAuthorisedSignatory = "Sample Exports Pvt Ltd",
        });

        var customer = await CreateCustomerAsync(client);

        // Deliberately sends none of those fields: the point is that the document picks them up.
        var invoice = await CreateAsync(client, customer.Id, new
        {
            customerId = customer.Id,
            invoiceDate = Today.ToString("yyyy-MM-dd"),
            items = ReferenceLines,
        });

        invoice.IecNumber.Should().Be("AAICN4546R");
        invoice.GstNumber.Should().Be("09AAICN4546R1ZL");
        invoice.ApedaRegistrationNumber.Should().Be("01780");
        invoice.CountryOfOrigin.Should().Be("INDIA");
        invoice.TermsOfPayment.Should().Be("T/T");
        invoice.PortOfLoading.Should().Be("VARANASI AIRPORT");
        invoice.AuthorisedSignatory.Should().Be("Sample Exports Pvt Ltd");
    }

    [Fact]
    public async Task A_value_on_the_document_beats_the_saved_default()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);

        await client.PutAsJsonAsync("/api/import-export/profile", new
        {
            defaultPortOfLoading = "VARANASI AIRPORT",
            defaultCountryOfOrigin = "INDIA",
        });

        var customer = await CreateCustomerAsync(client);
        var invoice = await CreateAsync(client, customer.Id, new
        {
            customerId = customer.Id,
            invoiceDate = Today.ToString("yyyy-MM-dd"),
            portOfLoading = "NHAVA SHEVA",
            items = ReferenceLines,
        });

        // The person filling the form in knows something about THIS shipment that the settings do
        // not; the setting still applies to everything they did not override.
        invoice.PortOfLoading.Should().Be("NHAVA SHEVA");
        invoice.CountryOfOrigin.Should().Be("INDIA");
    }

    [Fact]
    public async Task The_trade_profile_is_private_to_each_business()
    {
        var owner = await _factory.CreateSignedInClientAsync(connectPayments: false);
        await owner.PutAsJsonAsync("/api/import-export/profile", new { iecNumber = "OWNER-IEC" });

        var stranger = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var theirs = await stranger.GetFromJsonAsync<TradeProfileDto>(
            "/api/import-export/profile", QuotelyApiFactory.Json);

        theirs!.IecNumber.Should().BeNull();
    }

    [Fact]
    public async Task Declaration_wording_is_offered_as_a_suggestion_and_never_applied_on_its_own()
    {
        var client = await _factory.CreateSignedInClientAsync(connectPayments: false);
        var customer = await CreateCustomerAsync(client);

        var profile = await client.GetFromJsonAsync<TradeProfileDto>(
            "/api/import-export/profile", QuotelyApiFactory.Json);

        profile!.SuggestedHeaderDeclarations.Should().NotBeEmpty();
        profile.SuggestedFooterDeclaration.Should().NotBeNullOrWhiteSpace();

        var invoice = await CreateAsync(client, customer.Id, new
        {
            customerId = customer.Id,
            invoiceDate = Today.ToString("yyyy-MM-dd"),
            items = ReferenceLines,
        });

        // The IGST wording on the reference is true for that exporter under a dated notification.
        // Quotely cannot know it is true for anyone else, so nothing writes it to a document that
        // did not ask for it.
        invoice.HeaderDeclarations.Should().BeNull();
        invoice.FooterDeclaration.Should().BeNull();
    }

    // =====================================================================
    // Unauthenticated
    // =====================================================================

    [Fact]
    public async Task The_trade_endpoints_require_a_signed_in_business()
    {
        var anonymous = _factory.CreateClient();

        (await anonymous.GetAsync("/api/import-export/invoices"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/import-export/profile"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
