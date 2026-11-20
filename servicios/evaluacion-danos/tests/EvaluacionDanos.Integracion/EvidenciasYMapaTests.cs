using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EvaluacionDanos.Api.Controllers;
using EvaluacionDanos.Aplicacion;
using EvaluacionDanos.Dominio;
using EvaluacionDanos.Infraestructura;
using EvaluacionDanos.Infraestructura.Persistencia;
using Microsoft.EntityFrameworkCore;
using static EvaluacionDanos.Integracion.Lotes;

namespace EvaluacionDanos.Integracion;

[Collection(ColeccionApi.Nombre)]
public class EvidenciasYMapaTests(Entorno api)
{
    [Fact]
    public async Task Foto_con_el_hash_declarado_se_guarda_y_el_reenvio_no_reescribe()
    {
        var foto = FotoJpeg();
        var op = Crear(Entorno.TomarActivoSembrado(), 1, foto: foto);
        var cliente = api.Cliente("foto-A");
        await Entorno.Sincronizar(cliente, Lote("foto-A", op));
        var evidenciaId = op.Evaluacion!.Evidencias![0].Id!.Value;

        var primera = await Subir(cliente, evidenciaId, foto);
        await using var db = api.NuevoContexto();
        var guardada = await db.Evidencias.SingleAsync(e => e.Id == evidenciaId);
        var clave = guardada.ClaveObjeto!;
        var escritura = (await api.ObjetoAsync(clave))!.Value.Modificado;
        await Task.Delay(1100);   // la fecha del objeto tiene precisión de un segundo
        // El reenvío entra por la otra réplica: las dos comparten el almacén de objetos (DA-06, AD-24).
        var segunda = await Subir(api.Cliente("foto-A", replica: 1), evidenciaId, foto);

        Assert.Equal(HttpStatusCode.OK, primera.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        Assert.True(guardada.Subida);
        Assert.NotNull(guardada.InstanteSubida);
        Assert.Equal($"evaluacion/{op.Evaluacion.Id}/{evidenciaId}", clave);
        var objeto = (await api.ObjetoAsync(clave))!.Value;
        Assert.Equal(foto, objeto.Contenido);
        Assert.Equal(escritura, objeto.Modificado);
    }

    [Fact]
    public async Task Foto_con_hash_distinto_responde_422_y_no_guarda()
    {
        var foto = FotoJpeg();
        var op = Crear(Entorno.TomarActivoSembrado(), 1, foto: foto);
        var cliente = api.Cliente("foto-B");
        await Entorno.Sincronizar(cliente, Lote("foto-B", op));
        var evidenciaId = op.Evaluacion!.Evidencias![0].Id!.Value;

        var otra = FotoJpeg();
        var respuesta = await Subir(cliente, evidenciaId, otra);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        await using var db = api.NuevoContexto();
        Assert.False((await db.Evidencias.SingleAsync(e => e.Id == evidenciaId)).Subida);
        Assert.Null(await api.ObjetoAsync($"evaluacion/{op.Evaluacion.Id}/{evidenciaId}"));
    }

    [Fact]
    public async Task Foto_no_declarada_responde_404()
    {
        var respuesta = await Subir(api.Cliente("foto-C"), Guid.NewGuid(), FotoJpeg());
        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    [Fact]
    public async Task Foto_mas_grande_que_el_maximo_responde_413()
    {
        var grande = FotoJpeg(11 * 1024 * 1024);
        var op = Crear(Entorno.TomarActivoSembrado(), 1, foto: grande);
        var cliente = api.Cliente("foto-D");
        await Entorno.Sincronizar(cliente, Lote("foto-D", op));

        var respuesta = await Subir(cliente, op.Evaluacion!.Evidencias![0].Id!.Value, grande);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, respuesta.StatusCode);
    }

    private static async Task<HttpResponseMessage> Subir(HttpClient cliente, Guid evidenciaId, byte[] foto)
    {
        using var contenido = new MultipartFormDataContent();
        var archivo = new ByteArrayContent(foto);
        archivo.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        contenido.Add(archivo, "archivo", "foto.jpg");
        return await cliente.PutAsync($"/v1/evidencias/{evidenciaId}", contenido);
    }
}
