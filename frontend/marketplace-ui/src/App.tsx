/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { Suspense, lazy, useEffect } from 'react';
import { BrowserRouter, Routes, Route, Navigate, useLocation } from 'react-router-dom';
import { AuthProvider, useAuth } from './context/AuthContext';
import { ToastProvider, useToast } from './context/ToastContext';
import { setForbiddenHandler } from './api/client';
import Layout from './components/layout/Layout';
import LoginPage from './pages/auth/LoginPage';
import SinAccesoPage from './pages/auth/SinAccesoPage';

const DashboardPage = lazy(() => import('./pages/dashboard/DashboardPage'));
const OrdenesPage = lazy(() => import('./pages/ordenes/OrdenesPage'));
const WmsPage = lazy(() => import('./pages/wms/WmsPage'));
const FulfillmentPage = lazy(() => import('./pages/fulfillment/FulfillmentPage'));
const RmaPage = lazy(() => import('./pages/rma/RmaPage'));
const ComprasPage = lazy(() => import('./pages/compras/ComprasPage'));
const ContabilidadPage = lazy(() => import('./pages/contabilidad/ContabilidadPage'));
const LiquidacionesPage = lazy(() => import('./pages/liquidaciones/LiquidacionesPage'));
const ProductosPage = lazy(() => import('./pages/productos/ProductosPage'));
const ReabastecimientoPage = lazy(() => import('./pages/reabastecimiento/ReabastecimientoPage'));
const ProveedoresPage = lazy(() => import('./pages/proveedores/ProveedoresPage'));
const AjustesPage = lazy(() => import('./pages/ajustes/AjustesPage'));
const ConteosPage = lazy(() => import('./pages/conteos/ConteosPage'));
const AlertasInventarioPage = lazy(() => import('./pages/alertas/AlertasInventarioPage'));
const BloqueosInventarioPage = lazy(() => import('./pages/bloqueos/BloqueosInventarioPage'));

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { isAuthenticated } = useAuth();
  return isAuthenticated ? <>{children}</> : <Navigate to="/login" replace />;
}

function RoleRouteGuard({
  allowedRoles,
  children,
}: {
  allowedRoles: string[];
  children: React.ReactNode;
}) {
  const { isAuthenticated, usuario } = useAuth();
  const location = useLocation();

  if (!isAuthenticated) {
    return <Navigate to="/login" state={{ from: location }} replace />;
  }

  const currentRole = usuario?.rol || '';

  if (!allowedRoles.includes(currentRole)) {
    return <SinAccesoPage />;
  }

  return <>{children}</>;
}

function HomeRedirect() {
  const { usuario, isAuthenticated } = useAuth();

  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }

  if (usuario?.rol === 'Comprador') {
    return <Navigate to="/sin-acceso" replace />;
  }

  if (usuario?.rol === 'Vendedor') {
    return <Navigate to="/ordenes" replace />;
  }

  return <Navigate to="/dashboard" replace />;
}

function GlobalForbiddenListener() {
  const toast = useToast();

  useEffect(() => {
    setForbiddenHandler((msg: string) => {
      toast.error('Acceso denegado (403)', msg);
    });
  }, [toast]);

  return null;
}
function RouteLoading() {
  return (
    <div style={{ padding: 24 }} aria-live="polite" aria-busy="true">
      <div className="skeleton" style={{ height: 22, width: 220, borderRadius: 5, marginBottom: 12 }} />
      <div className="skeleton" style={{ height: 12, width: 360, maxWidth: '80%', borderRadius: 4, marginBottom: 24 }} />
      <div className="skeleton" style={{ height: 180, width: '100%', borderRadius: 8 }} />
    </div>
  );
}

function AppRoutes() {
  const { isAuthenticated } = useAuth();

  return (
    <Suspense fallback={<RouteLoading />}>
      <Routes>
        <Route path="/login" element={isAuthenticated ? <HomeRedirect /> : <LoginPage />} />

        <Route path="/" element={
          <ProtectedRoute>
            <Layout />
          </ProtectedRoute>
        }>
          <Route index element={<HomeRedirect />} />
          <Route path="dashboard" element={<RoleRouteGuard allowedRoles={['Admin']}><DashboardPage /></RoleRouteGuard>} />
          <Route path="ordenes" element={<RoleRouteGuard allowedRoles={['Admin', 'Vendedor']}><OrdenesPage /></RoleRouteGuard>} />
          <Route path="wms" element={<RoleRouteGuard allowedRoles={['Admin', 'Vendedor']}><WmsPage /></RoleRouteGuard>} />
          <Route path="reabastecimiento" element={<RoleRouteGuard allowedRoles={['Admin', 'Vendedor']}><ReabastecimientoPage /></RoleRouteGuard>} />
          <Route path="proveedores" element={<RoleRouteGuard allowedRoles={['Admin']}><ProveedoresPage /></RoleRouteGuard>} />
          <Route path="ajustes" element={<RoleRouteGuard allowedRoles={['Admin']}><AjustesPage /></RoleRouteGuard>} />
          <Route path="conteos" element={<RoleRouteGuard allowedRoles={['Admin', 'Vendedor']}><ConteosPage /></RoleRouteGuard>} />
          <Route path="alertas-inventario" element={<RoleRouteGuard allowedRoles={['Admin', 'Vendedor']}><AlertasInventarioPage /></RoleRouteGuard>} />
          <Route path="bloqueos-inventario" element={<RoleRouteGuard allowedRoles={['Admin', 'Vendedor']}><BloqueosInventarioPage /></RoleRouteGuard>} />
          <Route path="fulfillment" element={<RoleRouteGuard allowedRoles={['Admin', 'Vendedor']}><FulfillmentPage /></RoleRouteGuard>} />
          <Route path="rma" element={<RoleRouteGuard allowedRoles={['Admin', 'Vendedor']}><RmaPage /></RoleRouteGuard>} />
          <Route path="compras" element={<RoleRouteGuard allowedRoles={['Admin']}><ComprasPage /></RoleRouteGuard>} />
          <Route path="contabilidad" element={<RoleRouteGuard allowedRoles={['Admin']}><ContabilidadPage /></RoleRouteGuard>} />
          <Route path="liquidaciones" element={<RoleRouteGuard allowedRoles={['Admin', 'Vendedor']}><LiquidacionesPage /></RoleRouteGuard>} />
          <Route path="productos" element={<RoleRouteGuard allowedRoles={['Admin', 'Vendedor']}><ProductosPage /></RoleRouteGuard>} />
          <Route path="sin-acceso" element={<SinAccesoPage />} />
        </Route>

        <Route path="*" element={<HomeRedirect />} />
      </Routes>
    </Suspense>
  );
}

export default function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <ToastProvider>
          <AppRoutes />
        </ToastProvider>
      </AuthProvider>
    </BrowserRouter>
  );
}
