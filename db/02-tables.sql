-- 02: dbo base tables (no access for mcp_reader). Money = DECIMAL(19,4), dates = DATE.
USE HarborSupply;
GO

IF OBJECT_ID(N'dbo.Customer', N'U') IS NULL
CREATE TABLE dbo.Customer (
    CustomerId       INT           NOT NULL CONSTRAINT PK_Customer PRIMARY KEY,
    CustomerCode     VARCHAR(10)   NOT NULL CONSTRAINT UQ_Customer_Code UNIQUE,
    Name             VARCHAR(100)  NOT NULL,
    City             VARCHAR(60)   NOT NULL,
    State            CHAR(2)       NOT NULL,
    PaymentTermsDays INT           NOT NULL,
    CreatedDate      DATE          NOT NULL,
    TaxId            VARCHAR(20)   NOT NULL,   -- sensitive: never in a reporting view
    CreditLimit      DECIMAL(19,4) NOT NULL    -- sensitive: never in a reporting view
);
GO

IF OBJECT_ID(N'dbo.Vendor', N'U') IS NULL
CREATE TABLE dbo.Vendor (
    VendorId         INT           NOT NULL CONSTRAINT PK_Vendor PRIMARY KEY,
    VendorCode       VARCHAR(10)   NOT NULL CONSTRAINT UQ_Vendor_Code UNIQUE,
    Name             VARCHAR(100)  NOT NULL,
    City             VARCHAR(60)   NOT NULL,
    State            CHAR(2)       NOT NULL,
    PaymentTermsDays INT           NOT NULL,
    TaxId            VARCHAR(20)   NOT NULL,   -- sensitive
    RemitBankAccount VARCHAR(30)   NOT NULL    -- sensitive (synthetic value)
);
GO

IF OBJECT_ID(N'dbo.Item', N'U') IS NULL
CREATE TABLE dbo.Item (
    ItemId      INT           NOT NULL CONSTRAINT PK_Item PRIMARY KEY,
    Sku         VARCHAR(20)   NOT NULL CONSTRAINT UQ_Item_Sku UNIQUE,
    Description VARCHAR(100)  NOT NULL,
    Category    VARCHAR(40)   NOT NULL,
    ListPrice   DECIMAL(19,4) NOT NULL,
    UnitCost    DECIMAL(19,4) NOT NULL         -- sensitive: margin
);
GO

IF OBJECT_ID(N'dbo.Invoice', N'U') IS NULL
CREATE TABLE dbo.Invoice (
    InvoiceId   INT         NOT NULL CONSTRAINT PK_Invoice PRIMARY KEY,
    InvoiceNo   VARCHAR(20) NOT NULL CONSTRAINT UQ_Invoice_No UNIQUE,
    CustomerId  INT         NOT NULL CONSTRAINT FK_Invoice_Customer REFERENCES dbo.Customer (CustomerId),
    InvoiceDate DATE        NOT NULL,
    DueDate     DATE        NOT NULL,
    Status      VARCHAR(10) NOT NULL CONSTRAINT CK_Invoice_Status CHECK (Status IN ('Open', 'Paid', 'Void'))
);
GO

IF OBJECT_ID(N'dbo.InvoiceLine', N'U') IS NULL
CREATE TABLE dbo.InvoiceLine (
    InvoiceLineId  INT           NOT NULL CONSTRAINT PK_InvoiceLine PRIMARY KEY,
    InvoiceId      INT           NOT NULL CONSTRAINT FK_InvoiceLine_Invoice REFERENCES dbo.Invoice (InvoiceId),
    [LineNo]       INT           NOT NULL,         -- LINENO is a reserved word: always bracket
    ItemId         INT           NOT NULL CONSTRAINT FK_InvoiceLine_Item REFERENCES dbo.Item (ItemId),
    Quantity       INT           NOT NULL,
    UnitPrice      DECIMAL(19,4) NOT NULL,
    ExtendedAmount DECIMAL(19,4) NOT NULL,
    CONSTRAINT UQ_InvoiceLine_InvoiceLineNo UNIQUE (InvoiceId, [LineNo])
);
GO

IF OBJECT_ID(N'dbo.CustomerPayment', N'U') IS NULL
CREATE TABLE dbo.CustomerPayment (
    PaymentId   INT           NOT NULL CONSTRAINT PK_CustomerPayment PRIMARY KEY,
    CustomerId  INT           NOT NULL CONSTRAINT FK_CustomerPayment_Customer REFERENCES dbo.Customer (CustomerId),
    PaymentDate DATE          NOT NULL,
    Amount      DECIMAL(19,4) NOT NULL,
    Method      VARCHAR(10)   NOT NULL
);
GO

IF OBJECT_ID(N'dbo.PaymentApplication', N'U') IS NULL
CREATE TABLE dbo.PaymentApplication (
    PaymentId     INT           NOT NULL CONSTRAINT FK_PaymentApplication_Payment REFERENCES dbo.CustomerPayment (PaymentId),
    InvoiceId     INT           NOT NULL CONSTRAINT FK_PaymentApplication_Invoice REFERENCES dbo.Invoice (InvoiceId),
    AppliedAmount DECIMAL(19,4) NOT NULL,
    CONSTRAINT PK_PaymentApplication PRIMARY KEY (PaymentId, InvoiceId)
);
GO

IF OBJECT_ID(N'dbo.Bill', N'U') IS NULL
CREATE TABLE dbo.Bill (
    BillId   INT           NOT NULL CONSTRAINT PK_Bill PRIMARY KEY,
    BillNo   VARCHAR(20)   NOT NULL CONSTRAINT UQ_Bill_No UNIQUE,
    VendorId INT           NOT NULL CONSTRAINT FK_Bill_Vendor REFERENCES dbo.Vendor (VendorId),
    BillDate DATE          NOT NULL,
    DueDate  DATE          NOT NULL,
    Amount   DECIMAL(19,4) NOT NULL,
    Status   VARCHAR(10)   NOT NULL CONSTRAINT CK_Bill_Status CHECK (Status IN ('Open', 'Paid'))
);
GO

IF OBJECT_ID(N'dbo.BillPayment', N'U') IS NULL
CREATE TABLE dbo.BillPayment (
    BillPaymentId INT           NOT NULL CONSTRAINT PK_BillPayment PRIMARY KEY,
    BillId        INT           NOT NULL CONSTRAINT FK_BillPayment_Bill REFERENCES dbo.Bill (BillId),
    PaymentDate   DATE          NOT NULL,
    Amount        DECIMAL(19,4) NOT NULL
);
GO
