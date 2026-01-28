using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;

namespace Consumo_App.Controllers
{
    [ApiController]
    [Route("api/documentos")]
    [Authorize]
    public class DocumentosDetalleController : ControllerBase
    {
        private readonly string _connectionString;

        public DocumentosDetalleController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")!;
        }


        // GET /api/documentos/cxc/{id}        
        [HttpGet("cxc/{id:int}")]
        public async Task<IActionResult> GetDetalleCxc(int id)
        {
            using var conn = new SqlConnection(_connectionString);

            // Obtener documento
            var documentoSql = @"
                SELECT 
                    d.Id,
                    d.NumeroDocumento,
                    d.FechaEmision,
                    d.FechaVencimiento,
                    d.PeriodoDesde,
                    d.PeriodoHasta,
                    d.MontoTotal,
                    d.MontoPagado,
                    d.MontoPendiente,
                    d.Estado,
                    d.CantidadConsumos,
                    d.CantidadEmpleados,
                    d.Refinanciado,
                    d.FechaRefinanciamiento,
                    d.Notas,
                    d.Anulado,
                    d.CreadoUtc,
                    e.Id AS EmpresaId,
                    e.Nombre AS EmpresaNombre,
                    e.Rnc AS EmpresaRnc,
                    e.Telefono AS EmpresaTelefono,
                    e.Email AS EmpresaEmail,
                    e.Direccion AS EmpresaDireccion,
                    u.Nombre AS CreadoPor
                FROM CxcDocumentos d
                INNER JOIN Empresas e ON d.EmpresaId = e.Id
                LEFT JOIN Usuarios u ON d.CreadoPorUsuarioId = u.Id
                WHERE d.Id = @Id";

            var documento = await conn.QueryFirstOrDefaultAsync<DocumentoCxcDetalleDto>(documentoSql, new { Id = id });

            if (documento == null)
                return NotFound(new { message = "Documento no encontrado" });

            // Obtener consumos del documento usando la tabla de detalle
            var consumosSql = @"
                SELECT 
                    c.Id,
                    c.Fecha,
                    det.Monto,
                    c.Nota,
                    c.Referencia,
                    c.Concepto,
                    c.Reversado,
                    c.MotivoReverso,
                    cl.Id AS ClienteId,
                    cl.Codigo AS ClienteCodigo,
                    cl.Nombre AS ClienteNombre,
                    cl.Cedula AS ClienteCedula,
                    cl.Grupo AS ClienteGrupo,
                    p.Nombre AS ProveedorNombre,
                    t.Nombre AS TiendaNombre,
                    u.Nombre AS RegistradoPor
                FROM CxcDocumentoDetalles det
                INNER JOIN Consumos c ON det.ConsumoId = c.Id
                INNER JOIN Clientes cl ON c.ClienteId = cl.Id
                INNER JOIN Proveedores p ON c.ProveedorId = p.Id
                LEFT JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                LEFT JOIN Usuarios u ON c.UsuarioRegistradorId = u.Id
                WHERE det.CxcDocumentoId = @DocumentoId
                ORDER BY c.Fecha DESC, cl.Nombre";

            var consumos = await conn.QueryAsync<ConsumoDetalleDto>(consumosSql, new { DocumentoId = id });

            // Resumen por empleado
            var resumenEmpleadosSql = @"
                SELECT 
                    cl.Id AS ClienteId,
                    cl.Codigo,
                    cl.Nombre,
                    cl.Grupo,
                    COUNT(*) AS TotalConsumos,
                    SUM(det.Monto) AS MontoTotal
                FROM CxcDocumentoDetalles det
                INNER JOIN Consumos c ON det.ConsumoId = c.Id
                INNER JOIN Clientes cl ON c.ClienteId = cl.Id
                WHERE det.CxcDocumentoId = @DocumentoId
                GROUP BY cl.Id, cl.Codigo, cl.Nombre, cl.Grupo
                ORDER BY MontoTotal DESC";

            var resumenEmpleados = await conn.QueryAsync<ResumenEmpleadoDto>(resumenEmpleadosSql, new { DocumentoId = id });

            // Resumen por proveedor
            var resumenProveedoresSql = @"
                SELECT 
                    p.Id AS ProveedorId,
                    p.Nombre AS ProveedorNombre,
                    COUNT(*) AS TotalConsumos,
                    SUM(det.Monto) AS MontoTotal
                FROM CxcDocumentoDetalles det
                INNER JOIN Consumos c ON det.ConsumoId = c.Id
                INNER JOIN Proveedores p ON c.ProveedorId = p.Id
                WHERE det.CxcDocumentoId = @DocumentoId
                GROUP BY p.Id, p.Nombre
                ORDER BY MontoTotal DESC";

            var resumenProveedores = await conn.QueryAsync<ResumenProveedorDto>(resumenProveedoresSql, new { DocumentoId = id });

            // Historial de pagos del documento
            var pagosSql = @"
                SELECT 
                    p.Id,
                    p.NumeroRecibo,
                    p.Fecha,
                    p.Monto,
                    p.MetodoPago,
                    p.Referencia,
                    p.Banco,
                    p.Notas,
                    u.Nombre AS RegistradoPor
                FROM CxcPagos p
                LEFT JOIN Usuarios u ON p.RegistradoPorUsuarioId = u.Id
                WHERE p.CxcDocumentoId = @DocumentoId AND p.Anulado = 0
                ORDER BY p.Fecha DESC";

            var pagos = await conn.QueryAsync<PagoDocumentoDto>(pagosSql, new { DocumentoId = id });

            return Ok(new
            {
                documento,
                consumos,
                resumenEmpleados,
                resumenProveedores,
                pagos,
                totales = new
                {
                    totalConsumos = consumos.Count(),
                    montoConsumos = consumos.Sum(c => c.Monto),
                    empleadosUnicos = resumenEmpleados.Count(),
                    proveedoresUnicos = resumenProveedores.Count()
                }
            });
        }


        // GET /api/documentos/cxp/{id}

        [HttpGet("cxp/{id:int}")]
        public async Task<IActionResult> GetDetalleCxp(int id)
        {
            using var conn = new SqlConnection(_connectionString);

            // Obtener documento
            var documentoSql = @"
                SELECT 
                    d.Id,
                    d.NumeroDocumento,
                    d.NumeroFacturaProveedor,
                    d.Tipo,
                    d.FechaEmision,
                    d.FechaVencimiento,
                    d.PeriodoDesde,
                    d.PeriodoHasta,
                    d.MontoBruto,
                    d.MontoComision,
                    d.MontoTotal,
                    d.MontoPagado,
                    d.MontoPendiente,
                    d.Estado,
                    d.CantidadConsumos,
                    d.Concepto,
                    d.Notas,
                    d.Anulado,
                    d.CreadoUtc,
                    p.Id AS ProveedorId,
                    p.Nombre AS ProveedorNombre,
                    p.Rnc AS ProveedorRnc,
                    p.Telefono AS ProveedorTelefono,
                    p.Email AS ProveedorEmail,
                    p.Direccion AS ProveedorDireccion,
                    p.PorcentajeComision,
                    u.Nombre AS CreadoPor
                FROM CxpDocumentos d
                INNER JOIN Proveedores p ON d.ProveedorId = p.Id
                LEFT JOIN Usuarios u ON d.CreadoPorUsuarioId = u.Id
                WHERE d.Id = @Id";

            var documento = await conn.QueryFirstOrDefaultAsync<DocumentoCxpDetalleDto>(documentoSql, new { Id = id });

            if (documento == null)
                return NotFound(new { message = "Documento no encontrado" });

            // Obtener consumos del documento usando la tabla de detalle
            var consumosSql = @"
                SELECT 
                    c.Id,
                    c.Fecha,
                    det.MontoBruto AS Monto,
                    det.MontoNeto AS MontoNetoProveedor,
                    det.MontoComision,
                    c.PorcentajeComision,
                    c.Nota,
                    c.Referencia,
                    c.Concepto,
                    c.Reversado,
                    cl.Id AS ClienteId,
                    cl.Codigo AS ClienteCodigo,
                    cl.Nombre AS ClienteNombre,
                    e.Id AS EmpresaId,
                    e.Nombre AS EmpresaNombre,
                    t.Nombre AS TiendaNombre,
                    ca.Nombre AS CajaNombre,
                    u.Nombre AS RegistradoPor
                FROM CxpDocumentoDetalles det
                INNER JOIN Consumos c ON det.ConsumoId = c.Id
                INNER JOIN Clientes cl ON c.ClienteId = cl.Id
                INNER JOIN Empresas e ON c.EmpresaId = e.Id
                LEFT JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                LEFT JOIN ProveedorCajas ca ON c.CajaId = ca.Id
                LEFT JOIN Usuarios u ON c.UsuarioRegistradorId = u.Id
                WHERE det.CxpDocumentoId = @DocumentoId
                ORDER BY c.Fecha DESC";

            var consumos = await conn.QueryAsync<ConsumoCxpDetalleDto>(consumosSql, new { DocumentoId = id });

            // Resumen por empresa
            var resumenEmpresasSql = @"
                SELECT 
                    e.Id AS EmpresaId,
                    e.Nombre AS EmpresaNombre,
                    e.Rnc AS EmpresaRnc,
                    COUNT(*) AS TotalConsumos,
                    SUM(det.MontoBruto) AS MontoBruto,
                    SUM(det.MontoNeto) AS MontoNeto,
                    SUM(det.MontoComision) AS MontoComision
                FROM CxpDocumentoDetalles det
                INNER JOIN Consumos c ON det.ConsumoId = c.Id
                INNER JOIN Empresas e ON c.EmpresaId = e.Id
                WHERE det.CxpDocumentoId = @DocumentoId
                GROUP BY e.Id, e.Nombre, e.Rnc
                ORDER BY MontoBruto DESC";

            var resumenEmpresas = await conn.QueryAsync<ResumenEmpresaCxpDto>(resumenEmpresasSql, new { DocumentoId = id });

            // Resumen por tienda
            var resumenTiendasSql = @"
                SELECT 
                    t.Id AS TiendaId,
                    t.Nombre AS TiendaNombre,
                    COUNT(*) AS TotalConsumos,
                    SUM(det.MontoBruto) AS MontoBruto,
                    SUM(det.MontoNeto) AS MontoNeto
                FROM CxpDocumentoDetalles det
                INNER JOIN Consumos c ON det.ConsumoId = c.Id
                INNER JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                WHERE det.CxpDocumentoId = @DocumentoId
                GROUP BY t.Id, t.Nombre
                ORDER BY MontoBruto DESC";

            var resumenTiendas = await conn.QueryAsync<ResumenTiendaCxpDto>(resumenTiendasSql, new { DocumentoId = id });

            // Historial de pagos del documento
            var pagosSql = @"
    SELECT 
        p.Id,
        p.NumeroComprobante,
        p.Fecha,
        p.Monto,
        p.MetodoPago,
        p.Referencia,
        p.BancoOrigen,
        p.CuentaDestino,
        p.Notas,
        u.Nombre AS RegistradoPor
    FROM CxpPagos p
    LEFT JOIN Usuarios u ON p.RegistradoPorUsuarioId = u.Id
    WHERE p.CxpDocumentoId = @DocumentoId AND p.Anulado = 0
    ORDER BY p.Fecha DESC";

            var pagos = await conn.QueryAsync<PagoDocumentoCxpDto>(pagosSql, new { DocumentoId = id });

            return Ok(new
            {
                documento,
                consumos,
                resumenEmpresas,
                resumenTiendas,
                pagos,
                totales = new
                {
                    totalConsumos = consumos.Count(),
                    montoBruto = consumos.Sum(c => c.Monto),
                    montoNeto = consumos.Sum(c => c.MontoNetoProveedor),
                    montoComision = consumos.Sum(c => c.MontoComision),
                    empresasUnicas = resumenEmpresas.Count(),
                    tiendasUnicas = resumenTiendas.Count()
                }
            });
        }


        // GET /api/documentos/cxc/{id}/consumos

        [HttpGet("cxc/{id:int}/consumos")]
        public async Task<IActionResult> GetConsumosCxc(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
        {
            using var conn = new SqlConnection(_connectionString);

            // Obtener info basica del documento
            var docSql = @"
                SELECT d.Id, d.NumeroDocumento,
                       e.Nombre AS EmpresaNombre, e.Rnc AS EmpresaRnc,
                       d.PeriodoDesde, d.PeriodoHasta
                FROM CxcDocumentos d
                INNER JOIN Empresas e ON d.EmpresaId = e.Id
                WHERE d.Id = @Id";

            var doc = await conn.QueryFirstOrDefaultAsync<dynamic>(docSql, new { Id = id });
            if (doc == null)
                return NotFound(new { message = "Documento no encontrado" });

            // Contar total
            var countSql = @"
                SELECT COUNT(*) 
                FROM CxcDocumentoDetalles 
                WHERE CxcDocumentoId = @DocumentoId";

            var total = await conn.QueryFirstAsync<int>(countSql, new { DocumentoId = id });

            // Obtener consumos paginados
            var consumosSql = @"
                SELECT 
                    c.Id,
                    c.Fecha,
                    det.Monto,
                    c.Nota,
                    c.Referencia,
                    cl.Codigo AS ClienteCodigo,
                    cl.Nombre AS ClienteNombre,
                    cl.Grupo AS ClienteGrupo,
                    p.Nombre AS ProveedorNombre,
                    t.Nombre AS TiendaNombre
                FROM CxcDocumentoDetalles det
                INNER JOIN Consumos c ON det.ConsumoId = c.Id
                INNER JOIN Clientes cl ON c.ClienteId = cl.Id
                INNER JOIN Proveedores p ON c.ProveedorId = p.Id
                LEFT JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                WHERE det.CxcDocumentoId = @DocumentoId
                ORDER BY c.Fecha DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var consumos = await conn.QueryAsync<dynamic>(consumosSql, new
            {
                DocumentoId = id,
                Offset = (page - 1) * pageSize,
                PageSize = pageSize
            });

            return Ok(new
            {
                documento = new
                {
                    id,
                    numeroDocumento = doc.NumeroDocumento,
                    empresaNombre = doc.EmpresaNombre,
                    empresaRnc = doc.EmpresaRnc,
                    periodoDesde = doc.PeriodoDesde,
                    periodoHasta = doc.PeriodoHasta
                },
                consumos,
                pagination = new
                {
                    page,
                    pageSize,
                    total,
                    totalPages = (int)Math.Ceiling(total / (double)pageSize)
                }
            });
        }


        // GET /api/documentos/cxp/{id}/consumos

        [HttpGet("cxp/{id:int}/consumos")]
        public async Task<IActionResult> GetConsumosCxp(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 100)
        {
            using var conn = new SqlConnection(_connectionString);

            // Obtener info basica del documento
            var docSql = @"
                SELECT d.Id, d.NumeroDocumento,
                       p.Nombre AS ProveedorNombre, p.Rnc AS ProveedorRnc,
                       d.PeriodoDesde, d.PeriodoHasta
                FROM CxpDocumentos d
                INNER JOIN Proveedores p ON d.ProveedorId = p.Id
                WHERE d.Id = @Id";

            var doc = await conn.QueryFirstOrDefaultAsync<dynamic>(docSql, new { Id = id });
            if (doc == null)
                return NotFound(new { message = "Documento no encontrado" });

            // Contar total
            var countSql = @"
                SELECT COUNT(*) 
                FROM CxpDocumentoDetalles 
                WHERE CxpDocumentoId = @DocumentoId";

            var total = await conn.QueryFirstAsync<int>(countSql, new { DocumentoId = id });

            // Obtener consumos paginados
            var consumosSql = @"
                SELECT 
                    c.Id,
                    c.Fecha,
                    det.MontoBruto AS Monto,
                    det.MontoNeto AS MontoNetoProveedor,
                    det.MontoComision,
                    c.Nota,
                    cl.Codigo AS ClienteCodigo,
                    cl.Nombre AS ClienteNombre,
                    e.Nombre AS EmpresaNombre,
                    t.Nombre AS TiendaNombre,
                    ca.Nombre AS CajaNombre
                FROM CxpDocumentoDetalles det
                INNER JOIN Consumos c ON det.ConsumoId = c.Id
                INNER JOIN Clientes cl ON c.ClienteId = cl.Id
                INNER JOIN Empresas e ON c.EmpresaId = e.Id
                LEFT JOIN ProveedorTiendas t ON c.TiendaId = t.Id
                LEFT JOIN ProveedorCajas ca ON c.CajaId = ca.Id
                WHERE det.CxpDocumentoId = @DocumentoId
                ORDER BY c.Fecha DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            var consumos = await conn.QueryAsync<dynamic>(consumosSql, new
            {
                DocumentoId = id,
                Offset = (page - 1) * pageSize,
                PageSize = pageSize
            });

            return Ok(new
            {
                documento = new
                {
                    id,
                    numeroDocumento = doc.NumeroDocumento,
                    proveedorNombre = doc.ProveedorNombre,
                    proveedorRnc = doc.ProveedorRnc,
                    periodoDesde = doc.PeriodoDesde,
                    periodoHasta = doc.PeriodoHasta
                },
                consumos,
                pagination = new
                {
                    page,
                    pageSize,
                    total,
                    totalPages = (int)Math.Ceiling(total / (double)pageSize)
                }
            });
        }
    }

    #region DTOs

    public class DocumentoCxcDetalleDto
    {
        public int Id { get; set; }
        public string NumeroDocumento { get; set; } = "";
        public DateTime FechaEmision { get; set; }
        public DateTime FechaVencimiento { get; set; }
        public DateTime PeriodoDesde { get; set; }
        public DateTime PeriodoHasta { get; set; }
        public decimal MontoTotal { get; set; }
        public decimal MontoPagado { get; set; }
        public decimal MontoPendiente { get; set; }
        public int Estado { get; set; }
        public int CantidadConsumos { get; set; }
        public int CantidadEmpleados { get; set; }
        public bool Refinanciado { get; set; }
        public DateTime? FechaRefinanciamiento { get; set; }
        public string? Notas { get; set; }
        public bool Anulado { get; set; }
        public DateTime CreadoUtc { get; set; }
        public int EmpresaId { get; set; }
        public string EmpresaNombre { get; set; } = "";
        public string? EmpresaRnc { get; set; }
        public string? EmpresaTelefono { get; set; }
        public string? EmpresaEmail { get; set; }
        public string? EmpresaDireccion { get; set; }
        public string? CreadoPor { get; set; }
    }

   /* public class ConsumoDetalleDto
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public decimal Monto { get; set; }
        public string? Nota { get; set; }
        public string? Referencia { get; set; }
        public string? Concepto { get; set; }
        public bool Reversado { get; set; }
        public string? MotivoReverso { get; set; }
        public int ClienteId { get; set; }
        public string ClienteCodigo { get; set; } = "";
        public string ClienteNombre { get; set; } = "";
        public string? ClienteCedula { get; set; }
        public string? ClienteGrupo { get; set; }
        public string ProveedorNombre { get; set; } = "";
        public string? TiendaNombre { get; set; }
        public string? RegistradoPor { get; set; }
    }*/

    public class ResumenEmpleadoDto
    {
        public int ClienteId { get; set; }
        public string Codigo { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string? Grupo { get; set; }
        public int TotalConsumos { get; set; }
        public decimal MontoTotal { get; set; }
    }

    public class ResumenProveedorDto
    {
        public int ProveedorId { get; set; }
        public string ProveedorNombre { get; set; } = "";
        public int TotalConsumos { get; set; }
        public decimal MontoTotal { get; set; }
    }

    public class PagoDocumentoDto
    {
        public int Id { get; set; }
        public string NumeroRecibo { get; set; } = "";
        public DateTime Fecha { get; set; }
        public decimal Monto { get; set; }
        public int MetodoPago { get; set; }
        public string? Referencia { get; set; }
        public string? Banco { get; set; }
        public string? Notas { get; set; }
        public string? RegistradoPor { get; set; }
    }

    public class DocumentoCxpDetalleDto
    {
        public int Id { get; set; }
        public string NumeroDocumento { get; set; } = "";
        public string? NumeroFacturaProveedor { get; set; }
        public int Tipo { get; set; }
        public DateTime FechaEmision { get; set; }
        public DateTime FechaVencimiento { get; set; }
        public DateTime? PeriodoDesde { get; set; }
        public DateTime? PeriodoHasta { get; set; }
        public decimal MontoBruto { get; set; }
        public decimal MontoComision { get; set; }
        public decimal MontoTotal { get; set; }
        public decimal MontoPagado { get; set; }
        public decimal MontoPendiente { get; set; }
        public int Estado { get; set; }
        public int CantidadConsumos { get; set; }
        public string? Concepto { get; set; }
        public string? Notas { get; set; }
        public bool Anulado { get; set; }
        public DateTime CreadoUtc { get; set; }
        public int ProveedorId { get; set; }
        public string ProveedorNombre { get; set; } = "";
        public string? ProveedorRnc { get; set; }
        public string? ProveedorTelefono { get; set; }
        public string? ProveedorEmail { get; set; }
        public string? ProveedorDireccion { get; set; }
        public decimal PorcentajeComision { get; set; }
        public string? CreadoPor { get; set; }
    }

    public class ConsumoCxpDetalleDto
    {
        public int Id { get; set; }
        public DateTime Fecha { get; set; }
        public decimal Monto { get; set; }
        public decimal MontoNetoProveedor { get; set; }
        public decimal MontoComision { get; set; }
        public decimal PorcentajeComision { get; set; }
        public string? Nota { get; set; }
        public string? Referencia { get; set; }
        public string? Concepto { get; set; }
        public bool Reversado { get; set; }
        public int ClienteId { get; set; }
        public string ClienteCodigo { get; set; } = "";
        public string ClienteNombre { get; set; } = "";
        public int EmpresaId { get; set; }
        public string EmpresaNombre { get; set; } = "";
        public string? TiendaNombre { get; set; }
        public string? CajaNombre { get; set; }
        public string? RegistradoPor { get; set; }
    }

    public class ResumenEmpresaCxpDto
    {
        public int EmpresaId { get; set; }
        public string EmpresaNombre { get; set; } = "";
        public string? EmpresaRnc { get; set; }
        public int TotalConsumos { get; set; }
        public decimal MontoBruto { get; set; }
        public decimal MontoNeto { get; set; }
        public decimal MontoComision { get; set; }
    }

    public class ResumenTiendaCxpDto
    {
        public int TiendaId { get; set; }
        public string TiendaNombre { get; set; } = "";
        public int TotalConsumos { get; set; }
        public decimal MontoBruto { get; set; }
        public decimal MontoNeto { get; set; }
    }

    public class PagoDocumentoCxpDto
    {
        public int Id { get; set; }
        public string NumeroComprobante { get; set; } = "";  // Cambiado de NumeroTransaccion
        public DateTime Fecha { get; set; }
        public decimal Monto { get; set; }
        public int MetodoPago { get; set; }
        public string? Referencia { get; set; }
        public string? BancoOrigen { get; set; }  // Cambiado de Banco
        public string? CuentaDestino { get; set; }  // Nuevo campo
        public string? Notas { get; set; }
        public string? RegistradoPor { get; set; }
    }

    /* public class PagoDocumentoCxpDto
     {
         public int Id { get; set; }
         public string NumeroTransaccion { get; set; } = "";
         public DateTime Fecha { get; set; }
         public decimal Monto { get; set; }
         public int MetodoPago { get; set; }
         public string? Referencia { get; set; }
         public string? Banco { get; set; }
         public string? Notas { get; set; }
         public string? RegistradoPor { get; set; }
     } */

    #endregion
}