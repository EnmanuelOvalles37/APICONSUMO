using Dapper;
using Consumo_App.Data.Sql;
using Consumo_App.Models;

namespace Consumo_App.Servicios
{
    public class RbacSeeder
    {
        private static readonly (string Codigo, string Nombre, string Ruta)[] PERMISOS = new[]
        {
            ("buscar_cliente","Buscar Cliente","/clientes/cedula"),
            ("registrar_consumo","Registrar Consumo","/consumos"),
            ("consumos_ver","Ver Consumos","/consumos"),
            ("usuarios_password", "Cambiar contraseña de usuarios", "/admin/usuarios"),
            ("usuarios_ver", "Ver usuarios", "/admin/usuarios"),
            ("panel_ver","Panel Principal","/panel"),
            ("empresas_ver", "Empresas", "/empresas"),
            ("empresas_crear","Crear Empresa","/empresas/nueva"),
            ("empresas_editar","Editar Empresa","/empresas/:id"),
            ("clientes_ver","Gestión de Clientes","/"),
            ("guardar_clientes","Guardar Cliente","/guardar"),
            ("registro_ver","Registrar Consumo (form)","/registro"),
            ("cargar_csv","Cargar CSV de Clientes","/cargar_csv"),
            ("restablecer_saldos","Restablecer Saldos","/restablecer_saldos"),
            ("reporte_corte","Reporte Corte (form)","/reporte_corte"),
            ("reporte_corte_resultado","Reporte Corte (resultado)","/reporte_corte_resultado"),
            ("reporte_consumos","Reporte Consumos (form)","/reporte_consumos"),
            ("reporte_consumos_resultado","Reporte Consumos (resultado)","/reporte_consumos_resultado"),
            ("editar_cliente","Editar Cliente","/editar_cliente"),
            ("inactivar_grupo","Inactivar Grupo","/inactivar_grupo"),
            ("reactivar_grupo","Reactivar Grupo","/reactivar_grupo"),
            ("inactivar_cliente","Inactivar Cliente","/inactivar_cliente"),
            ("reactivar_cliente","Reactivar Cliente","/reactivar_cliente"),
            ("proveedores_usuarios","Proveedores & Usuarios","/proveedores_usuarios"),
            ("agregar_proveedor","Agregar Proveedor","/agregar_proveedor"),
            ("editar_proveedor","Editar Proveedor","/editar_proveedor"),
            ("agregar_usuario_proveedor","Agregar Usuario-Proveedor","/agregar_usuario_proveedor"),
            ("editar_usuario_proveedor","Editar Usuario-Proveedor","/editar_usuario_proveedor"),
            ("cuentas_por_pagar","Cuentas por Pagar","/cuentas_por_pagar"),
            ("exportar_cuentas_por_pagar_pdf","Exportar Cuentas por Pagar PDF","/exportar_cuentas_por_pagar_pdf"),
            ("aplicar_pago","Aplicar Pago a Proveedor (panel)","/aplicar_pago"),
            ("registrar_pago","Registrar Pago Proveedor","/registrar_pago"),
            ("balance_proveedor_api","Balance Proveedor (API)","/balance_proveedor"),
            ("cuentas_por_cobrar","Cuentas por Cobrar","/cuentas_por_cobrar"),
            ("deuda_por_grupo","Deuda por Grupo (API)","/deuda_por_grupo"),
            ("registrar_cobro","Registrar Cobro","/registrar_cobro"),
            ("reportes_generales","Reportes Generales (form)","/reportes_generales"),
            ("reporte_cuentas_por_cobrar","Reporte CxC (form)","/reporte_cuentas_por_cobrar"),
            ("reporte_antiguedad_cxc","Antigüedad CxC (form)","/reporte_antiguedad_cuentas_por_cobrar"),
            ("reporte_antiguedad_cxp","Antigüedad CxP (form)","/reporte_antiguedad_cxp"),
            ("exportar_pdf","Exportar Clientes PDF","/exportar_pdf"),
            ("exportar_usuarios_proveedores_pdf","Exportar Usuarios-Proveedores PDF","/exportar_usuarios_proveedores_pdf"),
            ("exportar_cuentas_por_cobrar_pdf","Exportar CxC PDF","/exportar_cuentas_por_cobrar_pdf"),
            ("exportar_reporte_consumos_pdf","Exportar Consumos PDF","/exportar_reporte_consumos_pdf"),
            ("reporte_general","Reporte General (resultado)","/reporte_general"),
            ("exportar_reporte_general_pdf","Exportar Reporte General PDF","/exportar_reporte_general_pdf"),
            ("exportar_antiguedad_pdf","Exportar Antigüedad CxC PDF","/exportar_antiguedad_pdf"),
            ("exportar_antiguedad_cuentas_por_pagar_pdf","Exportar Antigüedad CxP PDF","/exportar_antiguedad_cuentas_por_pagar_pdf"),
            ("reversar_consumo","Reversar Consumo (POST)","/reversar_consumo"),
            ("mostrar_formulario_reverso","Formulario Reverso (GET)","/reversar_consumo"),
            ("reporte_consumos_reversados","Consumos Reversados (listar)","/reporte_consumos_reversados"),
            ("exportar_consumos_reversados_pdf","Exportar Consumos Reversados PDF","/exportar_consumos_reversados_pdf"),
            ("reimprimir_consumo","Reimprimir Consumo","/reimprimir_consumo"),
            ("reporte_consumos_usuario_logueado","Reporte Consumos Usuario Logueado (form)","/reporte_consumos_usuario_logueado"),
            ("reporte_mi_consumo","Mi Consumo (form)","/reporte_mi_consumo"),
            ("reporte_mi_consumo_resultado","Mi Consumo (resultado)","/reporte_mi_consumo_resultado"),
            ("reporte_consumos_por_proveedor","Reporte por Proveedor (form)","/reporte_consumos_por_proveedor"),
            ("reporte_consumos_por_proveedor_resultado","Reporte por Proveedor (resultado)","/reporte_consumos_por_proveedor_resultado"),
            ("reporte_balance_proveedor","Reporte Balance Proveedor","/reporte_balance_proveedor"),
            ("ver_balance_proveedor","Ver Balance Proveedor","/ver_balance_proveedor"),
            ("admin_usuarios","Administrar Usuarios","/admin/usuarios"),
            ("admin_roles","Administrar Roles","/admin/roles"),
            ("crear_usuario_admin","Crear Usuario (Admin UI)","/crear_usuario"),
        };

        private static readonly (string Nombre, string Descripcion)[] ROLES = new[]
        {
            ("administrador","Control total"),
            ("usuario","Operación"),
            ("contabilidad","Contabilidad"),
            ("empleador","Empleador"),
            ("backoffice","Backoffice"),
        };

        private static readonly Dictionary<string, string[]> PERMISOS_POR_ROL = new()
        {
            ["administrador"] = PERMISOS.Select(x => x.Codigo).Distinct().ToArray(),
            ["usuario"] = new[] { "panel_ver", "registro_ver", "registrar_consumo", "reporte_mi_consumo", "reporte_mi_consumo_resultado", "reporte_consumos_usuario_logueado" },
            ["contabilidad"] = new[] { "cuentas_por_pagar", "registrar_pago", "aplicar_pago", "exportar_cuentas_por_pagar_pdf", "reporte_antiguedad_cxp", "cuentas_por_cobrar", "registrar_cobro", "reporte_antiguedad_cxc", "reportes_generales", "reporte_general", "exportar_reporte_general_pdf" },
            ["backoffice"] = new[] { "clientes_ver", "guardar_clientes", "editar_cliente", "agregar_proveedor", "editar_proveedor", "proveedores_usuarios" },
            ["empleador"] = new[] { "panel_ver", "reporte_mi_consumo", "reporte_mi_consumo_resultado" }
        };

        /// <summary>
        /// Ejecutar seed usando Dapper
        /// </summary>
        public static async Task SeedAsync(SqlConnectionFactory connectionFactory)
        {
            using var connection = connectionFactory.Create();
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // 1) Cargar estado actual
                var rolesDb = (await connection.QueryAsync<(int Id, string Nombre)>(
                    "SELECT Id, Nombre FROM Roles", transaction: transaction))
                    .ToDictionary(r => r.Nombre, r => r.Id, StringComparer.OrdinalIgnoreCase);

                var permisosDb = (await connection.QueryAsync<(int Id, string Codigo)>(
                    "SELECT Id, Codigo FROM Permisos", transaction: transaction))
                    .ToDictionary(p => p.Codigo, p => p.Id, StringComparer.OrdinalIgnoreCase);

                // 2) UPSERT Roles
                foreach (var (nombre, descripcion) in ROLES)
                {
                    if (!rolesDb.ContainsKey(nombre))
                    {
                        // Insertar nuevo rol
                        var rolId = await connection.ExecuteScalarAsync<int>(@"
                            INSERT INTO Roles (Nombre, Descripcion) 
                            OUTPUT INSERTED.Id 
                            VALUES (@Nombre, @Descripcion)",
                            new { Nombre = nombre, Descripcion = descripcion }, transaction);
                        rolesDb[nombre] = rolId;
                    }
                    else
                    {
                        // Actualizar descripción si cambió
                        await connection.ExecuteAsync(@"
                            UPDATE Roles SET Descripcion = @Descripcion WHERE Nombre = @Nombre",
                            new { Nombre = nombre, Descripcion = descripcion }, transaction);
                    }
                }

                // 3) UPSERT Permisos
                foreach (var (codigo, nombre, ruta) in PERMISOS.DistinctBy(p => p.Codigo))
                {
                    if (!permisosDb.ContainsKey(codigo))
                    {
                        var permisoId = await connection.ExecuteScalarAsync<int>(@"
                            INSERT INTO Permisos (Codigo, Nombre, Ruta) 
                            OUTPUT INSERTED.Id 
                            VALUES (@Codigo, @Nombre, @Ruta)",
                            new { Codigo = codigo, Nombre = nombre, Ruta = ruta }, transaction);
                        permisosDb[codigo] = permisoId;
                    }
                    else
                    {
                        await connection.ExecuteAsync(@"
                            UPDATE Permisos SET Nombre = @Nombre, Ruta = @Ruta WHERE Codigo = @Codigo",
                            new { Codigo = codigo, Nombre = nombre, Ruta = ruta }, transaction);
                    }
                }

                // 4) Asignar permisos por rol
                foreach (var (rolNombre, codigosPermisos) in PERMISOS_POR_ROL)
                {
                    if (!rolesDb.TryGetValue(rolNombre, out var rolId))
                        continue;

                    // Obtener permisos actuales del rol
                    var permisosActuales = (await connection.QueryAsync<int>(
                        "SELECT PermisoId FROM RolesPermisos WHERE RolId = @RolId",
                        new { RolId = rolId }, transaction)).ToHashSet();

                    // Calcular permisos objetivo
                    var permisosObjetivo = codigosPermisos
                        .Where(c => permisosDb.ContainsKey(c))
                        .Select(c => permisosDb[c])
                        .ToHashSet();

                    // Agregar los que faltan
                    var permisosAAgregar = permisosObjetivo.Except(permisosActuales).ToList();
                    if (permisosAAgregar.Count > 0)
                    {
                        var nuevosRolPermisos = permisosAAgregar.Select(pid => new { RolId = rolId, PermisoId = pid });
                        await connection.ExecuteAsync(
                            "INSERT INTO RolesPermisos (RolId, PermisoId) VALUES (@RolId, @PermisoId)",
                            nuevosRolPermisos, transaction);
                    }
                }

                // 5) Crear usuario admin por defecto si no existe
                var existeAdmin = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM Usuarios WHERE Nombre = 'admin'", transaction: transaction) > 0;

                if (!existeAdmin && rolesDb.TryGetValue("administrador", out var adminRolId))
                {
                    var hasher = new Pbkdf2Hasher();
                    await connection.ExecuteAsync(@"
                        INSERT INTO Usuarios (Nombre, Contrasena, RolId, Activo, AccessFailedCount) 
                        VALUES (@Nombre, @Contrasena, @RolId, 1, 0)",
                        new
                        {
                            Nombre = "admin",
                            Contrasena = hasher.Hash("admin"),
                            RolId = adminRolId
                        }, transaction);
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    /// <summary>
    /// Clase estática para métodos de extensión de RBAC
    /// </summary>
    public static class RbacSeederExtensions
    {
        /// <summary>
        /// Método de extensión para usar desde Program.cs
        /// </summary>
        public static async Task SeedRbacAsync(this WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var connectionFactory = scope.ServiceProvider.GetRequiredService<SqlConnectionFactory>();
            await RbacSeeder.SeedAsync(connectionFactory);
        }
    }
}