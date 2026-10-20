/**
 * ERP desarrollado por Marcelo Zepeda (@Marzza01)
 * Todos los derechos reservados © 2026
 */

import React, { useEffect } from 'react';
import { BrowserRouter, Routes, Route, Navigate, useLocation } from 'react-router-dom';
import { AuthProvider, useAuth } from './context/AuthContext';
import { ToastProvider, useToast } from './context/ToastContext';
import { setForbiddenHandler } from './api/client';
import Layout from './components/layout/Layout';
import LoginPage from './pages/auth/LoginPage';
import SinAccesoPage from './pages/auth/SinAccesoPage';
import DashboardPage from './pages/dashboard/DashboardPage';
import OrdenesPage from './pages/ordenes/OrdenesPage';
import WmsPage from './pages/wms/WmsPage';
import FulfillmentPage from './pages/fulfillment/FulfillmentPage';
import RmaPage from './pages/rma/RmaPage';
import ComprasPage from './pages/compras/ComprasPage';
import ContabilidadPage from './pages/contabilidad/ContabilidadPage';
import LiquidacionesPage from './pages/liquidaciones/LiquidacionesPage';
import ProductosPage from './pages/productos/ProductosPage';
import CheckoutPage from './pages/checkout/CheckoutPage';

// Guard de autenticación y autorización por rol
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

// Redirección inicial según rol
function HomeRedirect() {
  const { usuario, isAuthenticated } = useAuth();
  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }
  if (usuario?.rol === 'Comprador') {
    return <Navigate to="/checkout" replace />;
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

function AppRoutes() {
  const { isAuthenticated, usuario } = useAuth();

  return (
    <>
      <GlobalForbiddenListener />
      <Routes>
        <Route
          path="/login"
          element={
            isAuthenticated ? (
              usuario?.rol === 'Comprador' ? (
                <Navigate to="/checkout" replace />
              ) : (
                <Navigate to="/dashboard" replace />
              )
            ) : (
              <LoginPage />
            )
          }
        />

        <Route
          path="/"
          element={
            isAuthenticated ? (
              <Layout />
            ) : (
              <Navigate to="/login" replace />
            )
          }
        >
          <Route index element={<HomeRedirect />} />

          {/* Dashboard KPIs: Solo Admin */}
          <Route
            path="dashboard"
            element={
              <RoleRouteGuard allowedRoles={['Admin']}>
                <DashboardPage />
              </RoleRouteGuard>
            }
          />

          {/* Compras / 3-Way Match: Solo Admin */}
          <Route
            path="compras"
            element={
              <RoleRouteGuard allowedRoles={['Admin']}>
                <ComprasPage />
              </RoleRouteGuard>
            }
          />

          {/* Contabilidad: Solo Admin */}
          <Route
            path="contabilidad"
            element={
              <RoleRouteGuard allowedRoles={['Admin']}>
                <ContabilidadPage />
              </RoleRouteGuard>
            }
          />

          {/* Órdenes (OMS): Admin y Vendedor */}
          <Route
            path="ordenes"
            element={
              <RoleRouteGuard allowedRoles={['Admin', 'Vendedor']}>
                <OrdenesPage />
              </RoleRouteGuard>
            }
          />

          {/* WMS / Bodega: Admin y Vendedor */}
          <Route
            path="wms"
            element={
              <RoleRouteGuard allowedRoles={['Admin', 'Vendedor']}>
                <WmsPage />
              </RoleRouteGuard>
            }
          />

          {/* Fulfillment: Admin y Vendedor */}
          <Route
            path="fulfillment"
            element={
              <RoleRouteGuard allowedRoles={['Admin', 'Vendedor']}>
                <FulfillmentPage />
              </RoleRouteGuard>
            }
          />

          {/* RMA (Devoluciones): Admin y Vendedor */}
          <Route
            path="rma"
            element={
              <RoleRouteGuard allowedRoles={['Admin', 'Vendedor']}>
                <RmaPage />
              </RoleRouteGuard>
            }
          />

          {/* Liquidaciones: Admin y Vendedor */}
          <Route
            path="liquidaciones"
            element={
              <RoleRouteGuard allowedRoles={['Admin', 'Vendedor']}>
                <LiquidacionesPage />
              </RoleRouteGuard>
            }
          />

          {/* Productos: Admin y Vendedor */}
          <Route
            path="productos"
            element={
              <RoleRouteGuard allowedRoles={['Admin', 'Vendedor']}>
                <ProductosPage />
              </RoleRouteGuard>
            }
          />

          {/* Checkout / Catálogo de compra: Solo Comprador */}
          <Route
            path="checkout"
            element={
              <RoleRouteGuard allowedRoles={['Comprador']}>
                <CheckoutPage />
              </RoleRouteGuard>
            }
          />

          {/* Sin Acceso Explícito */}
          <Route path="sin-acceso" element={<SinAccesoPage />} />
        </Route>

        <Route path="*" element={<HomeRedirect />} />
      </Routes>
    </>
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
