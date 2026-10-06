// Menú del arrastre con el botón derecho del Explorador: "Copiar aquí con Espejo" y "Mover aquí con Espejo".
// Solo añade esas dos opciones y abre Espejo.exe (que está en la misma carpeta) con la orden; no copia nada ni se
// queda con nada del Explorador. Se registra por usuario desde Espejo (Configuración, clic derecho).
#include <windows.h>
#include <shlobj.h>
#include <shellapi.h>
#include <new>
#include <string>
#include <vector>

namespace
{
    // {7B1C5E2A-3F4D-4E8B-9A61-2C0D5E7F8A93}
    constexpr CLSID ClaseMenuArrastre = { 0x7b1c5e2a, 0x3f4d, 0x4e8b, { 0x9a, 0x61, 0x2c, 0x0d, 0x5e, 0x7f, 0x8a, 0x93 } };

    // Windows corta la línea de comandos en 32 767 caracteres: con muchos archivos se abre Espejo varias veces y
    // cada tanda entra en su cola.
    constexpr size_t LargoMaximoOrden = 30000;

    HMODULE modulo = nullptr;
    LONG objetosVivos = 0;

    enum Opcion : UINT { Copiar = 0, Mover = 1, CantidadOpciones = 2 };

    std::wstring RutaEspejo()
    {
        wchar_t ruta[MAX_PATH];
        const auto largo = GetModuleFileNameW(modulo, ruta, MAX_PATH);
        std::wstring carpeta(ruta, largo);

        return carpeta.substr(0, carpeta.find_last_of(L'\\') + 1) + L"Espejo.exe";
    }

    std::wstring EntreComillas(const std::wstring& texto)
    {
        // Una carpeta raíz ("D:\") acaba en barra: escaparla para que no se coma la comilla de cierre.
        return L"\"" + texto + (texto.ends_with(L'\\') ? L"\\" : L"") + L"\"";
    }

    void AbrirEspejo(const std::wstring& argumentos)
    {
        const auto espejo = RutaEspejo();
        ShellExecuteW(nullptr, L"open", espejo.c_str(), argumentos.c_str(), nullptr, SW_SHOWNORMAL);
    }

    class MenuArrastre final : public IShellExtInit, public IContextMenu
    {
    public:
        MenuArrastre() { InterlockedIncrement(&objetosVivos); }

        // IUnknown
        IFACEMETHODIMP QueryInterface(REFIID interfaz, void** resultado) override
        {
            if (interfaz == IID_IUnknown || interfaz == IID_IShellExtInit)
            {
                *resultado = static_cast<IShellExtInit*>(this);
            }
            else if (interfaz == IID_IContextMenu)
            {
                *resultado = static_cast<IContextMenu*>(this);
            }
            else
            {
                *resultado = nullptr;

                return E_NOINTERFACE;
            }

            AddRef();

            return S_OK;
        }

        IFACEMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&referencias); }

        IFACEMETHODIMP_(ULONG) Release() override
        {
            const auto quedan = InterlockedDecrement(&referencias);
            if (quedan == 0)
            {
                delete this;
            }

            return quedan;
        }

        // IShellExtInit: la carpeta donde se soltó y lo que se arrastró.
        IFACEMETHODIMP Initialize(PCIDLIST_ABSOLUTE carpeta, IDataObject* datos, HKEY) override
        {
            wchar_t ruta[MAX_PATH];
            if (carpeta == nullptr || datos == nullptr || !SHGetPathFromIDListW(carpeta, ruta))
            {
                return E_INVALIDARG;
            }

            destino = ruta;
            FORMATETC formato = { CF_HDROP, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL };
            STGMEDIUM medio = {};
            if (FAILED(datos->GetData(&formato, &medio)))
            {
                return E_INVALIDARG;
            }

            const auto lista = static_cast<HDROP>(GlobalLock(medio.hGlobal));
            const auto cantidad = lista ? DragQueryFileW(lista, 0xFFFFFFFF, nullptr, 0) : 0;
            for (UINT i = 0; i < cantidad; ++i)
            {
                std::wstring archivo(DragQueryFileW(lista, i, nullptr, 0), L'\0');
                DragQueryFileW(lista, i, archivo.data(), static_cast<UINT>(archivo.size() + 1));
                rutas.push_back(std::move(archivo));
            }

            GlobalUnlock(medio.hGlobal);
            ReleaseStgMedium(&medio);

            return rutas.empty() ? E_INVALIDARG : S_OK;
        }

        // IContextMenu
        IFACEMETHODIMP QueryContextMenu(HMENU menu, UINT posicion, UINT primerId, UINT, UINT banderas) override
        {
            if (banderas & CMF_DEFAULTONLY)
            {
                return MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL, 0);
            }

            InsertMenuW(menu, posicion, MF_BYPOSITION | MF_STRING, primerId + Copiar, L"Copiar aquí con Espejo");
            InsertMenuW(menu, posicion + 1, MF_BYPOSITION | MF_STRING, primerId + Mover, L"Mover aquí con Espejo");

            return MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL, CantidadOpciones);
        }

        IFACEMETHODIMP InvokeCommand(LPCMINVOKECOMMANDINFO info) override
        {
            if (HIWORD(info->lpVerb) != 0 || LOWORD(info->lpVerb) >= CantidadOpciones)
            {
                return E_FAIL;
            }

            const std::wstring verbo = LOWORD(info->lpVerb) == Mover ? L"mover" : L"copiar";
            const auto cierre = L" --destino " + EntreComillas(destino);
            std::wstring orden = verbo;
            for (const auto& ruta : rutas)
            {
                if (orden.size() > verbo.size() && orden.size() + ruta.size() + cierre.size() > LargoMaximoOrden)
                {
                    AbrirEspejo(orden + cierre);
                    orden = verbo;
                }

                orden += L" " + EntreComillas(ruta);
            }

            AbrirEspejo(orden + cierre);

            return S_OK;
        }

        IFACEMETHODIMP GetCommandString(UINT_PTR, UINT, UINT*, CHAR*, UINT) override { return E_NOTIMPL; }

    private:
        ~MenuArrastre() { InterlockedDecrement(&objetosVivos); }

        LONG referencias = 1;
        std::wstring destino;
        std::vector<std::wstring> rutas;
    };

    class Fabrica final : public IClassFactory
    {
    public:
        IFACEMETHODIMP QueryInterface(REFIID interfaz, void** resultado) override
        {
            if (interfaz == IID_IUnknown || interfaz == IID_IClassFactory)
            {
                *resultado = static_cast<IClassFactory*>(this);

                return S_OK;
            }

            *resultado = nullptr;

            return E_NOINTERFACE;
        }

        IFACEMETHODIMP_(ULONG) AddRef() override { return 2; }

        IFACEMETHODIMP_(ULONG) Release() override { return 1; }

        IFACEMETHODIMP CreateInstance(IUnknown* externo, REFIID interfaz, void** resultado) override
        {
            *resultado = nullptr;
            if (externo != nullptr)
            {
                return CLASS_E_NOAGGREGATION;
            }

            auto* menu = new (std::nothrow) MenuArrastre();
            if (menu == nullptr)
            {
                return E_OUTOFMEMORY;
            }

            const auto resultadoCreacion = menu->QueryInterface(interfaz, resultado);
            menu->Release();

            return resultadoCreacion;
        }

        IFACEMETHODIMP LockServer(BOOL bloquear) override
        {
            bloquear ? InterlockedIncrement(&objetosVivos) : InterlockedDecrement(&objetosVivos);

            return S_OK;
        }
    };

    Fabrica fabrica;
}

BOOL APIENTRY DllMain(HMODULE esteModulo, DWORD motivo, LPVOID)
{
    if (motivo == DLL_PROCESS_ATTACH)
    {
        modulo = esteModulo;
        DisableThreadLibraryCalls(esteModulo);
    }

    return TRUE;
}

STDAPI DllGetClassObject(REFCLSID clase, REFIID interfaz, void** resultado)
{
    if (clase != ClaseMenuArrastre)
    {
        *resultado = nullptr;

        return CLASS_E_CLASSNOTAVAILABLE;
    }

    return fabrica.QueryInterface(interfaz, resultado);
}

STDAPI DllCanUnloadNow()
{
    return objetosVivos == 0 ? S_OK : S_FALSE;
}
