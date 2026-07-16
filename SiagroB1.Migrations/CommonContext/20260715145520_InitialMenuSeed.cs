using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class InitialMenuSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PROFILES_ROLES_PROFILES_ProfileCode",
                table: "PROFILES_ROLES");

            migrationBuilder.DropForeignKey(
                name: "FK_ROLE_MENUS_ROLES_RoleCode",
                table: "ROLE_MENUS");

            migrationBuilder.DropForeignKey(
                name: "FK_ROLE_PERMISSIONS_ROLES_RoleCode",
                table: "ROLE_PERMISSIONS");

            migrationBuilder.DropIndex(
                name: "IX_ROLE_PERMISSIONS_RoleCode_PermissionCode",
                table: "ROLE_PERMISSIONS");

            migrationBuilder.DropIndex(
                name: "IX_PROFILES_ROLES_ProfileCode_RoleCode",
                table: "PROFILES_ROLES");

            migrationBuilder.AlterColumn<string>(
                name: "RoleCode",
                table: "ROLE_PERMISSIONS",
                type: "VARCHAR(50)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(50)");

            migrationBuilder.AlterColumn<string>(
                name: "RoleCode",
                table: "ROLE_MENUS",
                type: "VARCHAR(50)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(50)");

            migrationBuilder.AlterColumn<string>(
                name: "ProfileCode",
                table: "PROFILES_ROLES",
                type: "VARCHAR(50)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(50)");

            migrationBuilder.CreateIndex(
                name: "IX_ROLE_PERMISSIONS_RoleCode_PermissionCode",
                table: "ROLE_PERMISSIONS",
                columns: new[] { "RoleCode", "PermissionCode" },
                unique: true,
                filter: "[RoleCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PROFILES_ROLES_ProfileCode_RoleCode",
                table: "PROFILES_ROLES",
                columns: new[] { "ProfileCode", "RoleCode" },
                unique: true,
                filter: "[ProfileCode] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_PROFILES_ROLES_PROFILES_ProfileCode",
                table: "PROFILES_ROLES",
                column: "ProfileCode",
                principalTable: "PROFILES",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_ROLE_MENUS_ROLES_RoleCode",
                table: "ROLE_MENUS",
                column: "RoleCode",
                principalTable: "ROLES",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_ROLE_PERMISSIONS_ROLES_RoleCode",
                table: "ROLE_PERMISSIONS",
                column: "RoleCode",
                principalTable: "ROLES",
                principalColumn: "Code");
            
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns:
                [
                    "Key", 
                    "Title", 
                    "Icon", 
                    "Enabled", 
                    "Expanded",
                    "Order",
                    "ParentKey"
                ],
                values: new object[,]
                {
                    {"main", "Home",  "sap-icon://home", true, false, 0, null  },
                    {"admin", "Administração",  "sap-icon://folder-blank", true, false, 1, null  },
                    {"registers", "Cadastros",  "sap-icon://folder-blank", true, false, 2, null  },
                    {"storage", "Armazenagem",  "sap-icon://folder-blank", true, false, 3, null  },
                    {"purchases", "Compras",  "sap-icon://folder-blank", true, false, 4, null  },
                    {"sales", "Vendas",  "sap-icon://folder-blank", true, false, 5, null  },
                    {"reports", "Relatórios",  "sap-icon://folder-blank", true, false, 6, null  },
                    
                    {"branchs", "Filiais",  "sap-icon://folder-blank", true, false, 1, "admin"  },
                    {"systemSetup", "Configuração",  "sap-icon://folder-blank", true, false, 2, "admin"  },
                    {"docNumbers", "Numeração de Documentos",  "sap-icon://folder-blank", true, false, 3, "admin"  },
                    {"truckScales", "Balanças",  "sap-icon://folder-blank", true, false, 4, "admin"  },
                    {"states", "Estados",  "sap-icon://folder-blank", true, false, 5, "admin"  },
                    {"unitMeasure", "Unidades de Medida",  "sap-icon://folder-blank", true, false, 6, "admin"  },
                    {"menus", "Menus",  "sap-icon://folder-blank", true, false, 7, "admin"  },
                    {"permissions", "Permissões",  "sap-icon://folder-blank", true, false, 8, "admin"  },
                    {"roles", "Funções",  "sap-icon://folder-blank", true, false, 8, "admin"  },
                    {"profiles", "Perfis",  "sap-icon://folder-blank", true, false, 9, "admin"  },
                    {"users", "Usuários",  "sap-icon://folder-blank", true, false, 10, "admin"  },
                    
                    {"parceirosNegocio", "Parceiros de Negócio",  "sap-icon://folder-blank", true, false, 1, "registers"  },
                    {"agents", "Representantes",  "sap-icon://folder-blank", true, false, 2, "registers"  },
                    {"motoristas", "Motoristas",  "sap-icon://folder-blank", true, false, 3, "registers"  },
                    {"veiculos", "Veiculos",  "sap-icon://folder-blank", true, false, 4, "registers"  },
                    {"produtos", "Produtos",  "sap-icon://folder-blank", true, false, 5, "registers"  },
                    {"safras", "Safras",  "sap-icon://folder-blank", true, false, 6, "registers"  },
                    {"armazens", "Armazéns",  "sap-icon://folder-blank", true, false, 7, "registers"  },
                    {"logisticRegions", "Regiões Logistica",  "sap-icon://folder-blank", true, false, 8, "registers"  },
                    {"taxes", "Tributos/Impostos",  "sap-icon://folder-blank", true, false, 9, "registers"  },
                    {"qualityAttribs", "Atributos Qualitativos",  "sap-icon://folder-blank", true, false, 9, "registers"  },
                    {"processingServices", "Serviços de Armazenagem",  "sap-icon://folder-blank", true, false, 10, "registers"  },
                    
                    {"processingCostsList", "Tabela de Custos",  "sap-icon://folder-blank", true, false, 1, "storage"  },
                    {"storageAddresses", "Lotes de Armazenagem",  "sap-icon://folder-blank", true, false, 2, "storage"  },
                    {"weighingTickets", "Pesagem",  "sap-icon://folder-blank", true, false, 3, "storage"  },
                    {"weighingTicketsCompleted", "Tickets de Pesagem",  "sap-icon://folder-blank", true, false, 4, "storage"  },
                    {"storageTransactions", "Romaneios de Movimentação",  "sap-icon://folder-blank", true, false, 5, "storage"  },
                    {"ownershipTransfers", "Transferencia de Propriedade",  "sap-icon://folder-blank", true, false, 6, "storage"  },
                    {"storageInvoices", "Faturas de Serviço",  "sap-icon://folder-blank", true, false, 7, "storage"  },
                    {"storageAddressesReprocessing", "Reprocessar Saldo por Lote",  "sap-icon://folder-blank", true, false, 8, "storage"  },
                    {"storageAddressesDailyCalculation", "Reprocessar Calculo Diário",  "sap-icon://folder-blank", true, false, 9, "storage"  },
                    
                    {"purchaseContracts", "Contratos de Compra",  "sap-icon://folder-blank", true, false, 1, "purchases"  },
                    {"purchaseContractsApproval", "Aprovação de Contratos de Compra",  "sap-icon://folder-blank", true, false, 2, "purchases"  },
                    {"purchaseContractsShipmentRelease", "Contratos de Compra - Liberação de entregas",  "sap-icon://folder-blank", true, false, 3, "purchases"  },
                    {"shipmentReleases", "Liberações de Entrega de Contratos de Compras",  "sap-icon://folder-blank", true, false, 4, "purchases"  },
                    {"purchaseOrdersAllocations", "Alocação de Romaneios de Compra",  "sap-icon://folder-blank", true, false, 5, "purchases"  },
                    {"purchaseContractsAllocations", "Entregas de Contratos de Compra",  "sap-icon://folder-blank", true, false, 6, "purchases"  },
                    
                    {"salesContracts", "Contratos de Venda",  "sap-icon://folder-blank", true, false, 1, "sales"  },
                    {"salesContractsApproval", "Aprovação de Contratos de Venda",  "sap-icon://folder-blank", true, false, 2, "sales"  },
                    {"shippingTransaction", "Expedição de Grãos",  "sap-icon://folder-blank", true, false, 3, "sales"  },
                    {"shipmentBilling", "Faturamento de Expedição",  "sap-icon://folder-blank", true, false, 4, "sales"  },
                    {"salesInvoices", "Documentos de Saída",  "sap-icon://folder-blank", true, false, 5, "sales"  },
                    {"salesInvoicesOpenReconciliation", "Estornar Conferencia de entrega",  "sap-icon://folder-blank", true, false, 6, "sales"  },
                    {"salesInvoicesReconciliation", "Conferencia de entregas",  "sap-icon://folder-blank", true, false, 7, "sales"  },
                    
                    {"storageTransactionsReceiptsReport", "Romaneios de Entrada",  "sap-icon://folder-blank", true, false, 1, "reports"  },
                    {"storageTransactionsShipmentsReport", "Romaneios de Saída",  "sap-icon://folder-blank", true, false, 2, "reports"  },
                    {"storageDailyBalanceReport", "Saldo Diário por Lote",  "sap-icon://folder-blank", true, false, 3, "reports"  },
                    {"storageAddressesBalanceReport", "Saldos por Lote",  "sap-icon://folder-blank", true, false, 4, "reports"  },
                });

            migrationBuilder.InsertData(
                table: "PERMISSIONS",
                columns: 
                [
                    "Code",
                    "Description"
                ],
                values: new object[,]
                {
                    { "CREATE", "Create" },
                    { "DELETE", "Delete" },
                    { "READ", "Read" },
                    { "UPDATE", "Update" },
                }); 
            
            migrationBuilder.InsertData(
                table: "ROLES",
                columns: 
                [
                    "Code",
                    "Description"
                ],
                values: new object[,]
                {
                    { "ADMIN", "Administrador" }
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: 
                [
                    "Id",
                    "RoleCode",
                    "MenuItemKey"
                ],
                values: new object[,]
                {
                    {"B14A6C79-E16A-48A0-8B95-053949495FE7", "ADMIN", "branchs"},
                    {"18169217-4B1A-4606-BD03-069723D6DD67", "ADMIN", "docNumbers"},
                    {"C3C92CAB-0EB8-4FAF-B4B3-06FD94E0E666", "ADMIN", "shippingTransaction"},
                    {"B153BD2E-4113-477D-92A7-0CBA81E89320", "ADMIN", "taxes"},
                    {"8BA23F0C-FBBF-4D3C-9AA2-15D6DC1C073F", "ADMIN", "weighingTickets"},
                    {"F66441BC-919C-4CF3-B6BE-1BEF1AA10387", "ADMIN", "storageInvoices"},
                    {"3EC39619-2477-49F4-9CFA-298145A32563", "ADMIN", "storageTransactions"},
                    {"171DCE3D-524C-4FEA-93DF-2BD4A70F3869", "ADMIN", "reports"},
                    {"D5BF6C99-89C5-43F9-80A9-2FBE487131CE", "ADMIN", "storageAddressesBalanceReport"},
                    {"E06DF678-4473-475B-9577-3664AE8D74C1", "ADMIN", "salesInvoicesOpenReconciliation"},
                    {"794E94F0-EF3C-40CB-8705-3A231FE3E763", "ADMIN", "salesInvoices"},
                    {"620BD039-5762-4122-A27A-3AC0A6F1C5C2", "ADMIN", "storageAddressesDailyCalculation"},
                    {"2F50D59B-D9BD-4C07-A9CC-417FD73A96C0", "ADMIN", "purchaseContractsApproval"},
                    {"05DC39C0-C294-40A2-814F-444EDF238B4D", "ADMIN", "salesContracts"},
                    {"F7D59E1E-D87F-4208-97E1-4621BBB267A6", "ADMIN", "purchaseContractsShipmentRelease"},
                    {"BC4D96CE-EC18-44F4-A456-4707D8D413BC", "ADMIN", "shipmentBilling"},
                    {"7C34DDCC-4880-4E46-BC95-487A8B1CAEC6", "ADMIN", "processingCostsList"},
                    {"482B8E8F-E630-4280-9D06-4E72FEBE5A19", "ADMIN", "logisticRegions"},
                    {"C5B02BF1-0133-413F-8FB3-4F3318207AFE", "ADMIN", "purchaseContracts"},
                    {"84F28E78-5DA9-4FE0-8B6E-545B25E2F13B", "ADMIN", "qualityAttribs"},
                    {"E804966C-2301-484A-80C2-5816AFEAFD75", "ADMIN", "processingServices"},
                    {"498E1DDC-52CE-4816-AD04-5AE2912F8291", "ADMIN", "storageAddressesReprocessing"},
                    {"DA94741D-E6BC-4AB8-867F-5F454714EB9B", "ADMIN", "purchaseContractsAllocations"},
                    {"A0374EF6-73D2-4499-AF37-5FB9D8A5E041", "ADMIN", "admin"},
                    {"8195AF5D-AA24-4CFE-95A2-63EE6E307F00", "ADMIN", "purchases"},
                    {"E0038065-D79A-453F-99FD-66F1BED84603", "ADMIN", "storageAddresses"},
                    {"53995914-9E0F-40CB-9CF2-692238CCE778", "ADMIN", "armazens"},
                    {"5E64E279-BFC6-4669-8716-6DAAACA4FE44", "ADMIN", "storageTransactionsReceiptsReport"},
                    {"99526CF6-4766-4483-B084-71EB473ED217", "ADMIN", "roles"},
                    {"43E8F293-CAE0-4851-8CEA-75EA2FD30488", "ADMIN", "processingCostsList"},
                    {"DD37B3A8-7CF7-4260-A852-772AD182FB70", "ADMIN", "truckScales"},
                    {"72688CE7-4323-4271-AF7E-7D6D645D8DA2", "ADMIN", "parceirosNegocio"},
                    {"6BC732C8-A966-4B78-B154-7DFE8B39DE5C", "ADMIN", "purchaseOrdersAllocations"},
                    {"389007C0-F30B-46E6-B5DD-8217F79B128B", "ADMIN", "ownershipTransfers"},
                    {"839B6543-D4D2-4B0F-B951-833F3BCC1E80", "ADMIN", "salesContractsApproval"},
                    {"6F8CD80A-E537-4EC9-ADDC-85832AE11600", "ADMIN", "storageTransactionsShipmentsReport"},
                    {"6D1EFADC-0980-4935-9E3A-8BEF0ACB4E1E", "ADMIN", "registers"},
                    {"E7E1345C-543A-4815-A43C-8E73517D50CA", "ADMIN", "produtos"},
                    {"40F150D8-8EE7-4B4C-A802-8ECD1DA5918C", "ADMIN", "profiles"},
                    {"39367AC6-3789-4381-B374-8ED81C2A003D", "ADMIN", "agents"},
                    {"485CDB57-89BC-46CA-9514-9B52A4A4D4D5", "ADMIN", "storageDailyBalanceReport"},
                    {"0A907622-2083-49E8-87DB-9D4D0DBB4F05", "ADMIN", "motoristas"},
                    {"094B9033-0EAF-4D74-AE99-AE9849C3139C", "ADMIN", "shipmentReleases"},
                    {"4187E14D-7909-486B-B56B-AFE2A6B09FA1", "ADMIN", "systemSetup"},
                    {"E43DAEE8-AD20-4EE2-9943-B0ABE530A2C1", "ADMIN", "storage"},
                    {"C116A465-7BE2-4A69-9AD0-BD7013C942AB", "ADMIN", "weighingTicketsCompleted"},
                    {"08BD055F-921A-4CC2-8B8D-BDDE677D87CE", "ADMIN", "states"},
                    {"06670161-4D5D-417F-8C62-BFFDE5BD1E2C", "ADMIN", "menus"},
                    {"BB39B2CA-9581-401F-A2D6-C093696DCD72", "ADMIN", "shippingTransaction"},
                    {"3A9330D5-C8A4-4C8D-BD66-C24223523105", "ADMIN", "veiculos"},
                    {"25C5EAB6-0D17-41D1-80CD-D111C6495D87", "ADMIN", "unitMeasure"},
                    {"E92FCEF5-0551-43AD-BA72-E3478EE33ADE", "ADMIN", "salesInvoicesReconciliation"},
                    {"E3C03BFF-F27D-46F1-9724-E7F129862A56", "ADMIN", "users"},
                    {"75785082-5961-430B-B444-ED6FBA308978", "ADMIN", "permissions"},
                    {"1F0445C3-2A6A-4DC6-B97D-F343EE946465", "ADMIN", "safras"},
                    {"3231DA1C-D146-4454-91A0-F3C2CC8959E1", "ADMIN", "sales"},
                });

            migrationBuilder.InsertData(
                table: "ROLE_PERMISSIONS",
                columns:
                [
                    "Id",
                    "RoleCode",
                    "PermissionCode"
                ],
                values: new object[,]
                {
                    { "809AD85F-97BA-47CD-93A7-EC335CF5A284", "ADMIN", "CREATE"},
                    { "06215349-90DE-4D4C-B48C-B305C1191BFF", "ADMIN", "DELETE"},
                    { "3C67BE40-5925-4AD5-8D17-45E87BF4613F", "ADMIN", "READ"},
                    { "0F6156D8-7F86-4969-B7BF-2C1BBC801777", "ADMIN", "UPDATE"},
                });
            
            migrationBuilder.InsertData(
                table: "PROFILES",
                columns:
                [
                    "Code",
                    "Description",
                ],
                values: new object[,]
                {
                    { "ADMIN", "System Administrator"},
                });
            
            migrationBuilder.InsertData(
                table: "PROFILES_ROLES",
                columns:
                [
                    "Id",
                    "ProfileCode",
                    "RoleCode"
                ],
                values: new object[,]
                {
                    { "3C192678-9CE9-4D1B-A964-21F0ABB35835", "ADMIN", "ADMIN"},
                });
            
            migrationBuilder.Sql("""
                ------------------------------------------------------------
                -- Cria o usuário admin caso não exista
                ------------------------------------------------------------
                IF NOT EXISTS (
                    SELECT 1
                    FROM USERS
                    WHERE USERNAME = 'admin'
                )
                BEGIN
                    INSERT INTO USERS
                    (
                        ID,
                        USERNAME,
                        PASSWORDHASH,
                        FULLNAME,
                        EMAIL,
                        ISACTIVE,
                        ISADMIN,
                        CREATEDAT
                    )
                    VALUES
                    (
                        NEWID(),
                        'admin',
                        'A6xnQhbz4Vx2HuGl4lXwZ5U2I8iziLRFnhP5eNfIRvQ=',
                        'Administrador do Sistema',
                        NULL,
                        1,
                        1,
                        GETDATE()
                    );
                END;

                ------------------------------------------------------------
                -- Garante que o usuário admin esteja marcado como administrador
                ------------------------------------------------------------
                UPDATE USERS
                   SET ISADMIN = 1
                 WHERE USERNAME = 'admin'
                   AND ISADMIN = 0;

                ------------------------------------------------------------
                -- Vincula o profile ADMIN
                ------------------------------------------------------------
                IF NOT EXISTS
                (
                    SELECT 1
                      FROM USERS_PROFILES UP
                      JOIN USERS U
                        ON U.ID = UP.USERID
                     WHERE U.USERNAME = 'admin'
                       AND UP.PROFILECODE = 'ADMIN'
                )
                BEGIN
                    INSERT INTO USERS_PROFILES
                    (
                        ID,
                        USERID,
                        PROFILECODE
                    )
                    SELECT
                        NEWID(),
                        U.ID,
                        'ADMIN'
                    FROM USERS U
                    WHERE U.USERNAME = 'admin';
                END;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PROFILES_ROLES_PROFILES_ProfileCode",
                table: "PROFILES_ROLES");

            migrationBuilder.DropForeignKey(
                name: "FK_ROLE_MENUS_ROLES_RoleCode",
                table: "ROLE_MENUS");

            migrationBuilder.DropForeignKey(
                name: "FK_ROLE_PERMISSIONS_ROLES_RoleCode",
                table: "ROLE_PERMISSIONS");

            migrationBuilder.DropIndex(
                name: "IX_ROLE_PERMISSIONS_RoleCode_PermissionCode",
                table: "ROLE_PERMISSIONS");

            migrationBuilder.DropIndex(
                name: "IX_PROFILES_ROLES_ProfileCode_RoleCode",
                table: "PROFILES_ROLES");

            migrationBuilder.AlterColumn<string>(
                name: "RoleCode",
                table: "ROLE_PERMISSIONS",
                type: "VARCHAR(50)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "VARCHAR(50)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RoleCode",
                table: "ROLE_MENUS",
                type: "VARCHAR(50)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "VARCHAR(50)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProfileCode",
                table: "PROFILES_ROLES",
                type: "VARCHAR(50)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "VARCHAR(50)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ROLE_PERMISSIONS_RoleCode_PermissionCode",
                table: "ROLE_PERMISSIONS",
                columns: new[] { "RoleCode", "PermissionCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PROFILES_ROLES_ProfileCode_RoleCode",
                table: "PROFILES_ROLES",
                columns: new[] { "ProfileCode", "RoleCode" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PROFILES_ROLES_PROFILES_ProfileCode",
                table: "PROFILES_ROLES",
                column: "ProfileCode",
                principalTable: "PROFILES",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ROLE_MENUS_ROLES_RoleCode",
                table: "ROLE_MENUS",
                column: "RoleCode",
                principalTable: "ROLES",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ROLE_PERMISSIONS_ROLES_RoleCode",
                table: "ROLE_PERMISSIONS",
                column: "RoleCode",
                principalTable: "ROLES",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);
            
            migrationBuilder.Sql("""
                                 DELETE UP
                                 FROM USERS_PROFILES UP
                                 INNER JOIN USERS U
                                 ON U.ID = UP.USERID
                                 WHERE U.USERNAME = 'admin'
                                 AND UP.PROFILECODE = 'ADMIN';
                                 """);
        }
    }
}
