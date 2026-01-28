/*using Consumo_App.Data;
using Consumo_App.Models;


namespace Consumo_App.Data.Seeds
{
    public static class ProveedorSeed
    {
        public static async Task SeedAsync(AppDbContext db)
        {
            if (await db.Proveedores.AnyAsync()) return; // Ya existe data

            // Crear proveedor
            var proveedor = new Proveedor
            {
                Nombre = "Comercial La Fuente SRL",
                Rnc = "123456789",
                Activo = true
                
            };
            db.Proveedores.Add(proveedor);
            await db.SaveChangesAsync();

            // Crear tiendas
            var tienda1 = new ProveedorTienda { Nombre = "Sucursal Central", ProveedorId = proveedor.Id };
            var tienda2 = new ProveedorTienda { Nombre = "Sucursal Norte", ProveedorId = proveedor.Id };
            db.proveedorTiendas.AddRange(tienda1, tienda2);
            await db.SaveChangesAsync();

            // Crear cajas
            var caja1 = new ProveedorCaja { Nombre = "Caja 1 - Central", TiendaId = tienda1.Id };
            var caja2 = new ProveedorCaja { Nombre = "Caja 2 - Central", TiendaId = tienda1.Id };
            var caja3 = new ProveedorCaja { Nombre = "Caja 1 - Norte", TiendaId = tienda2.Id };
            db.proveedorCajas.AddRange(caja1, caja2, caja3);
            await db.SaveChangesAsync();

            // Crear usuarios proveedores
            var usuario1 = new Usuario { Nombre = "cajero_central", Contrasena = "1234", RolId = 3, Activo = true };
            var usuario2 = new Usuario { Nombre = "cajero_norte", Contrasena = "1234", RolId = 3, Activo = true };
            db.Usuarios.AddRange(usuario1, usuario2);
            await db.SaveChangesAsync();

            // Asignar usuarios a cajas
            db.ProveedorAsignaciones.AddRange(
                new ProveedorAsignacion
                {
                    ProveedorId = proveedor.Id,
                    TiendaId = tienda1.Id,
                    CajaId = caja1.Id,
                    UsuarioId = usuario1.Id
                },
                new ProveedorAsignacion
                {
                    ProveedorId = proveedor.Id,
                    TiendaId = tienda2.Id,
                    CajaId = caja3.Id,
                    UsuarioId = usuario2.Id
                }
            );

            await db.SaveChangesAsync();
        }
    }
}

*/