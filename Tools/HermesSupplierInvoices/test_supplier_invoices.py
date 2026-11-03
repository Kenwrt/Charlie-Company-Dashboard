"""Synthetic extraction and transport-boundary tests; no supplier/network access."""
import sys
import os
import unittest
from pathlib import Path

if os.environ.get('CCV_SUPPLIER_TEST_SOURCE'):
    sys.path.insert(0, os.environ['CCV_SUPPLIER_TEST_SOURCE'])
elif Path('/srv/ai/hermes/supplier-invoices').exists():
    sys.path.insert(0, '/srv/ai/hermes/supplier-invoices')
import supplier_invoices as worker
import suppli_portal as portal
import json
import decks_docks_parser as decks_parser


class PositionedInvoiceTests(unittest.TestCase):
    @staticmethod
    def fixture(legacy=False):
        def w(value, x, y, width=None, size=7):
            return {'text': value, 'x0': x, 'x1': x + (width if width is not None else len(value) * 3), 'top': y, 'bottom': y + size}
        words = [w('Invoice',420,50),w('No',447,50),w('DEMO-42',550,46,size=14),
                 w('Invoice',420,72),w('Date',448,72),w('05/18/2026',535,72),w('Terms',420,84),w('Net',550,84),w('30',565,84),
                 w('Line',35,310),w('Description',66,310),w('Qty/Footage',349,310),w('Price',428,310),
                 w('Per' if legacy else 'UOM',473,310),w('Total',535,310),
                 w('1',43,340),w('SKU-123',66,340),w('-',100,340),w('Example',110,340),w('board',139,340),
                 w('wrapped',66,351.5),w('description',95,351.5),w('3',370,340),w('Each',384,340),w('10.00',441,340),
                 w('Each',473,340),w('TX1',510,340),w('30.00',550,340)]
        if legacy:
            words += [w('Invoice',419,680),w('Amount',447,680),w('30.00',550,678.8),w('Discount',419,697),w('Amount',450,697),w('0.00',550,695.8),
                      w('Tax',419,716),w('Amount',434,716),w('2.70',550,714.8),w('Total',419,735),w('Invoice',440,735),w('32.70',550,733.8)]
        else:
            words += [w('Total',466,680),w('Amount',485,680),w('30.00',550,678.8),w('Sales',479,716),w('Tax',499,716),w('2.70',550,714.8),
                      w('Invoice',468,735),w('Total',494,735),w('32.70',550,733.8)]
        return {'width':612,'height':792,'words':words}

    def test_modern_layout_uses_real_columns_and_larger_invoice_number(self):
        result = decks_parser.parse_pages([self.fixture()])
        self.assertEqual(result['invoiceNumber'], 'DEMO-42')
        self.assertEqual(result['tax'], 2.70)
        self.assertEqual(result['total'], 32.70)
        self.assertEqual(result['dueDate'], '2026-06-17')
        line = result['lines'][0]
        self.assertEqual(line['sku'], 'SKU-123')
        self.assertEqual(line['quantity'], 3)
        self.assertEqual(line['purchaseUnit'], 'Each')
        self.assertEqual(line['description'], 'Example board wrapped description')
        self.assertTrue(result['extractionComplete'])

    def test_legacy_per_and_reversed_total_labels(self):
        result = decks_parser.parse_pages([self.fixture(True)])
        self.assertEqual(result['tax'], 2.70)
        self.assertEqual(result['total'], 32.70)
        self.assertEqual(result['lines'][0]['sku'], 'SKU-123')

    def test_legal_attachment_does_not_require_an_item_table(self):
        legal = {'width':612,'height':792,'words':[
            {'text':t,'x0':x,'x1':x+20,'top':30,'bottom':37} for t,x in [('Terms',30),('and',60),('Conditions',90)]]}
        self.assertTrue(decks_parser.parse_pages([self.fixture(),legal])['extractionComplete'])

    def test_different_pricing_unit_is_flagged_not_silently_converted(self):
        page = self.fixture()
        next(w for w in page['words'] if w['x0']==473 and w['text']=='Each')['text']='MBF'
        result = decks_parser.parse_pages([page])
        self.assertEqual(result['lines'][0]['quantity'],3)
        self.assertTrue(any('different units' in warning for warning in result['warnings']))
        self.assertFalse(result['extractionComplete'])


class InvoiceExtractionTests(unittest.TestCase):
    def test_suppli_graphql_queries_are_read_only(self):
        endpoint = 'https://core.gosuppli.com/graphql'
        self.assertTrue(portal.read_only_request('POST', endpoint, json.dumps({'query': 'query ReadInvoices { invoices { id } }'})))
        self.assertFalse(portal.read_only_request('POST', endpoint, json.dumps({'query': 'mutation ReadInvoices { payInvoice(id: 1) { id } }'})))
        document = 'query ReadInvoices { invoices { id } } mutation Pay { payInvoice(id: 1) { id } }'
        self.assertFalse(portal.read_only_request('POST', endpoint, json.dumps({'query': document, 'operationName': 'Pay'})))
        self.assertFalse(portal.read_only_request('POST', endpoint, json.dumps({'query': document})))
        self.assertTrue(portal.read_only_request('POST', endpoint, json.dumps({'query': document, 'operationName': 'ReadInvoices'})))

    def test_suppli_writes_and_unknown_persisted_queries_stay_blocked(self):
        self.assertFalse(portal.read_only_request('DELETE', 'https://core.gosuppli.com/graphql', '{}'))
        self.assertFalse(portal.read_only_request('POST', 'https://other.invalid/graphql', json.dumps({'query': '{ invoices { id } }'})))
        self.assertFalse(portal.read_only_request('POST', 'https://core.gosuppli.com/graphql', json.dumps({'operationName': 'ReadInvoices', 'extensions': {}})))
    def test_original_reference_is_preserved_without_inventing_operation_or_payment(self):
        text = 'DECKS & DOCKS\nInvoice No: DEMO-42\nInvoice Date: 10/30/2026\nDue Date: 11/29/2026\nYour Ref    Murfreesboro | Job 1042 | Example\n'
        result = worker.parse_invoice(text, {})
        self.assertEqual(result['yourReference'], 'Murfreesboro | Job 1042 | Example')
        self.assertIsNone(result['localOperationId'])
        self.assertFalse(result['paymentStateVerified'])
        self.assertFalse(result['extractionComplete'])
        self.assertEqual(result['jobNumber'], '')

    def test_blank_reference_does_not_consume_next_label(self):
        result = worker.parse_invoice('DECKS & DOCKS\nYour Ref   \nOur Ref   DEMO-1\n', {})
        self.assertEqual(result['yourReference'], '')

    def test_no_text_still_requires_review(self):
        result = worker.parse_invoice('', {})
        self.assertEqual(result['lines'], [])
        self.assertTrue(result['warnings'])
        self.assertFalse(result['extractionComplete'])

    def test_wrong_invoice_total_is_not_approved(self):
        text = 'DECKS & DOCKS\nSKU1  Example board  2 EA 10.00 20.00\nTAX 0.00\nFREIGHT 0.00\nDISCOUNT 0.00\nTOTAL 99.00\n'
        result = worker.parse_invoice(text, {})
        self.assertTrue(any('reconcile' in warning for warning in result['warnings']))
        self.assertFalse(result['paymentStateVerified'])

    def test_document_url_hosts_are_exact(self):
        hosts = {'suppli-prod.s3.amazonaws.com'}
        self.assertTrue(worker.allowed_url('https://suppli-prod.s3.amazonaws.com/example.pdf', hosts))
        self.assertFalse(worker.allowed_url('https://suppli-prod.s3.amazonaws.com.attacker.invalid/example.pdf', hosts))
        self.assertFalse(worker.allowed_url('http://suppli-prod.s3.amazonaws.com/example.pdf', hosts))
        self.assertFalse(worker.allowed_url('https://user:password@suppli-prod.s3.amazonaws.com/example.pdf', hosts))

    def test_transport_refuses_http_before_network_access(self):
        with self.assertRaises(worker.Blocked):
            worker.handoff({'dashboard_url': 'http://example.invalid'}, '', b'%PDF-test', {})

    def test_direct_origin_requires_private_address(self):
        with self.assertRaises(worker.Blocked):
            worker.dashboard_opener({'dashboard_url': 'https://example.invalid', 'dashboard_connect_address': '8.8.8.8'})
        with self.assertRaises(worker.Blocked):
            worker.dashboard_opener({'dashboard_url': 'https://example.invalid', 'dashboard_connect_address': 'not-an-ip'})


if __name__ == '__main__':
    unittest.main()
